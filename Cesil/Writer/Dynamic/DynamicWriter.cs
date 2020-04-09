﻿using System;
using System.Buffers;
using System.Collections.Generic;

using static Cesil.DisposableHelper;

namespace Cesil
{
    internal sealed class DynamicWriter :
        SyncWriterBase<dynamic>,
        IDelegateCache
    {
        internal new bool IsFirstRow => !ColumnNames.HasValue;

        private NonNull<Comparison<DynamicCellValue>> ColumnNameSorter;

        private NonNull<(string Name, string EncodedName)[]> ColumnNames;

        private readonly object[] DynamicArgumentsBuffer = new object[3];

        private Dictionary<object, Delegate>? DelegateCache;

        private bool HasWrittenComments;

        internal DynamicWriter(DynamicBoundConfiguration config, IWriterAdapter inner, object? context) : base(config, inner, context) { }

        CachedDelegate<V> IDelegateCache.TryGet<T, V>(T key)
            where V : class
        {
            if (DelegateCache == null)
            {
                return CachedDelegate<V>.Empty;
            }

            if (DelegateCache.TryGetValue(key, out var cached))
            {
                return new CachedDelegate<V>(cached as V);
            }

            return CachedDelegate<V>.Empty;
        }

        void IDelegateCache.Add<T, V>(T key, V cached)
        {
            if (DelegateCache == null)
            {
                DelegateCache = new Dictionary<object, Delegate>();
            }

            DelegateCache.Add(key, cached);
        }

        internal override void WriteInner(dynamic row)
        {
            try
            {
                WriteHeadersAndEndRowIfNeeded(row);

                var wholeRowContext = WriteContext.DiscoveringCells(Configuration.Options, RowNumber, Context);

                var options = Configuration.Options;

                var cellValues = options.TypeDescriber.GetCellsForDynamicRow(in wholeRowContext, row as object);
                cellValues = ForceInOrder(cellValues);

                var columnNamesValue = ColumnNames.Value;

                var i = 0;
                foreach (var cell in cellValues)
                {
                    var needsSeparator = i != 0;

                    if (needsSeparator)
                    {
                        PlaceCharInStaging(options.ValueSeparator);
                    }

                    ColumnIdentifier ci;
                    if (i < columnNamesValue.Length)
                    {
                        ci = ColumnIdentifier.CreateInner(i, columnNamesValue[i].Name);
                    }
                    else
                    {
                        ci = ColumnIdentifier.Create(i);
                    }

                    var ctx = WriteContext.WritingColumn(Configuration.Options, RowNumber, ci, Context);

                    var formatter = cell.Formatter;
                    var delProvider = (ICreatesCacheableDelegate<Formatter.DynamicFormatterDelegate>)formatter;
                    delProvider.Guarantee(this);
                    var del = delProvider.CachedDelegate.Value;

                    var val = cell.Value as object;
                    if (!del(val, in ctx, Buffer))
                    {
                        Throw.SerializationException<object>($"Could not write column {ci}, formatter {formatter} returned false");
                    }

                    ReadOnlySequence<char> res = default;
                    if (!Buffer.MakeSequence(ref res))
                    {
                        // nothing was written, so just move on
                        goto end;
                    }

                    WriteValue(res);
                    Buffer.Reset();

end:
                    i++;
                }

                RowNumber++;
            }
            catch (Exception e)
            {
                Throw.PoisonAndRethrow<object>(this, e);
            }
        }

        public override void WriteComment(string comment)
        {
            AssertNotDisposed(this);
            AssertNotPoisoned(Configuration);

            Utils.CheckArgumentNull(comment, nameof(comment));

            try
            {

                var shouldEndRecord = true;
                if (IsFirstRow)
                {
                    if (Configuration.Options.WriteHeader == WriteHeader.Always)
                    {
                        if (!HasWrittenComments)
                        {
                            shouldEndRecord = false;
                        }
                    }
                    else
                    {
                        if (!CheckHeaders(null))
                        {
                            shouldEndRecord = false;
                        }
                    }
                }

                if (shouldEndRecord)
                {
                    EndRecord();
                }

                var (commentChar, segments) = SplitCommentIntoLines(comment);

                // we know we can write directly now
                var isFirstRow = true;
                foreach (var seg in segments)
                {
                    HasWrittenComments = true;

                    if (!isFirstRow)
                    {
                        EndRecord();
                    }

                    PlaceCharInStaging(commentChar);
                    if (seg.Span.Length > 0)
                    {
                        PlaceAllInStaging(seg.Span);
                    }

                    isFirstRow = false;
                }
            }
            catch (Exception e)
            {
                Throw.PoisonAndRethrow<object>(this, e);
            }
        }

        private void WriteHeadersAndEndRowIfNeeded(dynamic row)
        {
            var shouldEndRecord = true;

            if (IsFirstRow)
            {
                if (!CheckHeaders(row))
                {
                    shouldEndRecord = false;
                }
            }

            if (shouldEndRecord)
            {
                EndRecord();
            }
        }

        private IEnumerable<DynamicCellValue> ForceInOrder(IEnumerable<DynamicCellValue> raw)
        {
            var columnNamesValue = ColumnNames.Value;

            // no headers mean we write whatever we're given!
            if (columnNamesValue.Length == 0) return raw;

            var inOrder = true;

            var i = 0;
            foreach (var x in raw)
            {
                if (i == columnNamesValue.Length)
                {
                    return Throw.InvalidOperationException<IEnumerable<DynamicCellValue>>("Too many cells returned, could not place in desired order");
                }

                var expectedName = columnNamesValue[i];
                if (!expectedName.Name.Equals(x.Name))
                {
                    inOrder = false;
                    break;
                }

                i++;
            }

            // already in order, 
            if (inOrder) return raw;

            var ret = new List<DynamicCellValue>(raw);
            ret.Sort(ColumnNameSorter.Value);

            return ret;
        }

        // returns true if it did write out headers,
        //   so we need to end a record before
        //   writing the next one
        private bool CheckHeaders(dynamic? firstRow)
        {
            if (Configuration.Options.WriteHeader == WriteHeader.Never)
            {
                // nothing to write, so bail
                ColumnNames.Value = Array.Empty<(string, string)>();
                return false;
            }

            // init columns
            DiscoverColumns(firstRow);

            WriteHeaders();

            return true;
        }

        private void DiscoverColumns(dynamic o)
        {
            var cols = new List<(string TrueName, string EncodedName)>();

            var ctx = WriteContext.DiscoveringColumns(Configuration.Options, Context);

            var options = Configuration.Options;

            var colIx = 0;
            foreach (var c in options.TypeDescriber.GetCellsForDynamicRow(in ctx, o as object))
            {
                var colName = c.Name;

                if (colName == null)
                {
                    Throw.InvalidOperationException<object>($"No column name found at index {colIx} when {nameof(Cesil.WriteHeader)} = {options.WriteHeader}");
                    return;
                }

                var encodedColName = colName;

                // encode it, if it needs encoding
                if (NeedsEncode(encodedColName))
                {
                    encodedColName = Utils.Encode(encodedColName, options);
                }

                cols.Add((colName, encodedColName));
            }

            ColumnNames.Value = cols.ToArray();

            ColumnNameSorter.Value =
                (a, b) =>
                {
                    var columnNamesValue = ColumnNames.Value;

                    int aIx = -1, bIx = -1;
                    for (var i = 0; i < columnNamesValue.Length; i++)
                    {
                        var colName = columnNamesValue[i].Name;
                        if (colName.Equals(a.Name))
                        {
                            aIx = i;
                            if (bIx != -1) break;
                        }

                        if (colName.Equals(b.Name))
                        {
                            bIx = i;
                            if (aIx != -1) break;
                        }
                    }

                    return aIx.CompareTo(bIx);
                };
        }

        private void WriteHeaders()
        {
            var columnNamesValue = ColumnNames.Value;
            for (var i = 0; i < columnNamesValue.Length; i++)
            {
                if (i != 0)
                {
                    // first value doesn't get a separator
                    PlaceCharInStaging(Configuration.Options.ValueSeparator);
                }
                else
                {
                    // if we're going to write any headers... before we 
                    //   write the first one we need to check if
                    //   we need to end the previous record... which only happens
                    //   if we've written comments _before_ the header
                    if (HasWrittenComments)
                    {
                        EndRecord();
                    }
                }

                var colName = columnNamesValue[i].EncodedName;

                // can colName is always gonna be encoded correctly, because we just discovered them
                //   (ie. they're always correct for this config)
                PlaceAllInStaging(colName.AsSpan());
            }
        }

        public override void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;

                try
                {

                    if (IsFirstRow)
                    {
                        CheckHeaders(null);
                    }

                    if (Configuration.Options.WriteTrailingRowEnding == WriteTrailingRowEnding.Always)
                    {
                        EndRecord();
                    }

                    if (HasStaging)
                    {
                        if (InStaging > 0)
                        {
                            FlushStaging();
                        }

                        Staging.Dispose();
                        Staging = EmptyMemoryOwner.Singleton;
                        StagingMemory = Memory<char>.Empty;
                    }

                    Inner.Dispose();
                    Buffer.Dispose();
                }
                catch (Exception e)
                {
                    if (HasStaging)
                    {
                        Staging.Dispose();
                        Staging = EmptyMemoryOwner.Singleton;
                        StagingMemory = Memory<char>.Empty;
                    }

                    Buffer.Dispose();

                    Throw.PoisonAndRethrow<object>(this, e);
                }
            }
        }

        public override string ToString()
        => $"{nameof(DynamicWriter)} with {Configuration}";
    }
}

﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Cesil
{
    /// <summary>
    /// Interface for a synchronous reader.
    /// </summary>
    public interface IReader<TRow> : IDisposable
    {
        /// <summary>
        /// Reads all rows into the provided collection, returning the entire set at once.
        /// 
        /// into must be non-null.
        /// </summary>
        TCollection ReadAll<TCollection>(TCollection into)
        where TCollection : class, ICollection<TRow>;

        /// <summary>
        /// Reads all rows, returning the entire set at once.
        /// </summary>
        List<TRow> ReadAll();

        /// <summary>
        /// Returns an enumerable that will read and yield
        /// one row at a time.
        /// 
        /// The returned IEnumerable(TRow) may only be enumerated once.
        /// </summary>
        IEnumerable<TRow> EnumerateAll();

        /// <summary>
        /// Reads a single row, populating row and returning true
        /// if a row was available and false otherwise.
        /// </summary>
        [return: IntentionallyExposedPrimitive("Most convenient way to indicate success, and fits the TryXXX pattern")]
        bool TryRead(
            [MaybeNullWhen(false)]
            out TRow row
        );

        /// <summary>
        /// Reads a single row into the existing instance of row,
        /// returning true if a row was available and false otherwise.
        /// 
        /// Row will be initialized if need be.
        /// 
        /// Note, it is possible for row to be initialized BUT for this method
        /// to return false.  In that case row should be ignored>
        /// </summary>
        [return: IntentionallyExposedPrimitive("Most convenient way to indicate success, and fits the TryXXX pattern")]
        bool TryReadWithReuse(ref TRow row);

        /// <summary>
        /// Reads a single row or comment.
        /// 
        /// Distinguish between a row, comment, or nothing by inspecting 
        /// ReadWithCommentResult(T).ResultType.
        /// 
        /// Note, it is possible for row to be initialized BUT for this method
        /// to return a comment or no value.  In that case row should be ignored.
        /// </summary>
        ReadWithCommentResult<TRow> TryReadWithComment();

        /// <summary>
        /// Reads a single row (storing into an existing instance of a row
        /// if provided) or comment.
        ///
        /// Distinguish between a row, comment, or nothing by inspecting 
        /// ReadWithCommentResult(T).ResultType.
        /// 
        /// Row will be initialized if need be.
        /// 
        /// Note, it is possible for row to be initialized BUT for this method
        /// to return a comment or no value.  In that case row should be ignored.
        /// </summary>
        ReadWithCommentResult<TRow> TryReadWithCommentReuse(ref TRow row);
    }
}

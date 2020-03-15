﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reflection;

namespace Cesil
{
    internal static class Types
    {
        internal delegate bool InstanceBuildThunkDelegate<T>(object thunk, out T val);

        internal static readonly TypeInfo ParserDelegateType = typeof(ParserDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo SetterDelegateType = typeof(SetterDelegate<,>).GetTypeInfo();
        internal static readonly TypeInfo StaticSetterDelegateType = typeof(StaticSetterDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo ResetDelegateType = typeof(ResetDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo StaticResetDelegateType = typeof(StaticResetDelegate).GetTypeInfo();
        internal static readonly TypeInfo GetterDelegateType = typeof(GetterDelegate<,>).GetTypeInfo();
        internal static readonly TypeInfo StaticGetterDelegateType = typeof(StaticGetterDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo FormatterDelegateType = typeof(FormatterDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo ShouldSerializeDelegateType = typeof(ShouldSerializeDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo StaticShouldSerializeDelegateType = typeof(StaticShouldSerializeDelegate).GetTypeInfo();
        internal static readonly TypeInfo DynamicRowConverterDelegateType = typeof(DynamicRowConverterDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo ParseAndSetOnDelegateType = typeof(ParseAndSetOnDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo MoveFromHoldToRowDelegateType = typeof(MoveFromHoldToRowDelegate<,>).GetTypeInfo();
        internal static readonly TypeInfo GetInstanceGivenHoldDelegateType = typeof(GetInstanceGivenHoldDelegate<,>).GetTypeInfo();
        internal static readonly TypeInfo ClearHoldDelegateType = typeof(ClearHoldDelegate<>).GetTypeInfo();

        internal static readonly TypeInfo VoidType = typeof(void).GetTypeInfo();
        internal static readonly TypeInfo BoolType = typeof(bool).GetTypeInfo();
        internal static readonly TypeInfo ByteType = typeof(byte).GetTypeInfo();
        internal static readonly TypeInfo SByteType = typeof(sbyte).GetTypeInfo();
        internal static readonly TypeInfo ShortType = typeof(short).GetTypeInfo();
        internal static readonly TypeInfo UShortType = typeof(ushort).GetTypeInfo();
        internal static readonly TypeInfo IntType = typeof(int).GetTypeInfo();
        internal static readonly TypeInfo UIntType = typeof(uint).GetTypeInfo();
        internal static readonly TypeInfo LongType = typeof(long).GetTypeInfo();
        internal static readonly TypeInfo ULongType = typeof(ulong).GetTypeInfo();
        internal static readonly TypeInfo FloatType = typeof(float).GetTypeInfo();
        internal static readonly TypeInfo DoubleType = typeof(double).GetTypeInfo();
        internal static readonly TypeInfo DecimalType = typeof(decimal).GetTypeInfo();
        internal static readonly TypeInfo ObjectType = typeof(object).GetTypeInfo();
        internal static readonly TypeInfo CharType = typeof(char).GetTypeInfo();
        internal static readonly TypeInfo StringType = typeof(string).GetTypeInfo();
        internal static readonly TypeInfo DateTimeType = typeof(DateTime).GetTypeInfo();
        internal static readonly TypeInfo IndexType = typeof(Index).GetTypeInfo();
        internal static readonly TypeInfo RangeType = typeof(Range).GetTypeInfo();
        internal static readonly TypeInfo IReadOnlyListType = typeof(IReadOnlyList<>).GetTypeInfo();

        internal static readonly TypeInfo[] TupleTypes =
            new TypeInfo[]
            {
                typeof(Tuple<>).GetTypeInfo(),
                typeof(Tuple<,>).GetTypeInfo(),
                typeof(Tuple<,,>).GetTypeInfo(),
                typeof(Tuple<,,,>).GetTypeInfo(),
                typeof(Tuple<,,,,>).GetTypeInfo(),
                typeof(Tuple<,,,,,>).GetTypeInfo(),
                typeof(Tuple<,,,,,,>).GetTypeInfo(),
                typeof(Tuple<,,,,,,,>).GetTypeInfo()
            };

        internal static readonly TypeInfo[] ValueTupleTypes =
           new TypeInfo[]
           {
                typeof(ValueTuple<>).GetTypeInfo(),
                typeof(ValueTuple<,>).GetTypeInfo(),
                typeof(ValueTuple<,,>).GetTypeInfo(),
                typeof(ValueTuple<,,,>).GetTypeInfo(),
                typeof(ValueTuple<,,,,>).GetTypeInfo(),
                typeof(ValueTuple<,,,,,>).GetTypeInfo(),
                typeof(ValueTuple<,,,,,,>).GetTypeInfo(),
                typeof(ValueTuple<,,,,,,,>).GetTypeInfo()
           };

        internal static readonly TypeInfo ReadOnlySpanOfCharType = typeof(ReadOnlySpan<char>).GetTypeInfo();
        internal static readonly TypeInfo IBufferWriterOfCharType = typeof(IBufferWriter<char>).GetTypeInfo();
        internal static readonly TypeInfo IEnumerableType = typeof(System.Collections.IEnumerable).GetTypeInfo();
        internal static readonly TypeInfo IEnumerableOfTType = typeof(IEnumerable<>).GetTypeInfo();
        internal static readonly TypeInfo IEquatableType = typeof(IEquatable<>).GetTypeInfo();
        internal static readonly TypeInfo IDisposableType = typeof(IDisposable).GetTypeInfo();

        internal static readonly TypeInfo[] ParserConstructorOneParameterTypes_Array = new[] { typeof(ReadOnlySpan<char>).GetTypeInfo() };

        internal static readonly TypeInfo[] ParserConstructorTwoParameterTypes_Array = new[] { typeof(ReadOnlySpan<char>).GetTypeInfo(), typeof(ReadContext).MakeByRefType().GetTypeInfo() };

        internal static readonly TypeInfo ColumnIdentifierType = typeof(ColumnIdentifier).GetTypeInfo();
        internal static readonly TypeInfo NonNullType = typeof(NonNull<>).GetTypeInfo();
        internal static readonly TypeInfo IDynamicRowOwnerType = typeof(IDynamicRowOwner).GetTypeInfo();
        internal static readonly TypeInfo RowEndingType = typeof(RowEnding).GetTypeInfo();
        internal static readonly TypeInfo ReadHeaderType = typeof(ReadHeader).GetTypeInfo();
        internal static readonly TypeInfo WriteHeaderType = typeof(WriteHeader).GetTypeInfo();
        internal static readonly TypeInfo WriteTrailingRowEndingType = typeof(WriteTrailingRowEnding).GetTypeInfo();
        internal static readonly TypeInfo ReadContextType = typeof(ReadContext).GetTypeInfo();
        internal static readonly TypeInfo WriteContextType = typeof(WriteContext).GetTypeInfo();
        internal static readonly TypeInfo DynamicRowDisposalType = typeof(DynamicRowDisposal).GetTypeInfo();
        internal static readonly TypeInfo ManualTypeDescriberFallbackBehaviorType = typeof(ManualTypeDescriberFallbackBehavior).GetTypeInfo();
        internal static readonly TypeInfo ExtraColumnTreatmentType = typeof(ExtraColumnTreatment).GetTypeInfo();

        internal static readonly TypeInfo InstanceProviderDelegateType = typeof(InstanceProviderDelegate<>).GetTypeInfo();
        internal static readonly TypeInfo NeedsHoldRowConstructorType = typeof(NeedsHoldRowConstructor<,>).GetTypeInfo();

        internal static readonly TypeInfo DefaultTypeParsersType = typeof(DefaultTypeParsers).GetTypeInfo();
        internal static readonly TypeInfo DefaultEnumTypeParserType = typeof(DefaultTypeParsers.DefaultEnumTypeParser<>).GetTypeInfo();
        internal static readonly TypeInfo DefaultFlagsEnumTypeParserType = typeof(DefaultTypeParsers.DefaultFlagsEnumTypeParser<>).GetTypeInfo();

        internal static readonly TypeInfo DefaultTypeFormattersType = typeof(DefaultTypeFormatters).GetTypeInfo();
        internal static readonly TypeInfo DefaultEnumTypeFormatterType = typeof(DefaultTypeFormatters.DefaultEnumTypeFormatter<>).GetTypeInfo();
        internal static readonly TypeInfo DefaultFlagsEnumTypeFormatterType = typeof(DefaultTypeFormatters.DefaultFlagsEnumTypeFormatter<>).GetTypeInfo();

        internal static readonly TypeInfo DynamicCellType = typeof(DynamicCell).GetTypeInfo();
        internal static readonly TypeInfo DynamicRowType = typeof(DynamicRow).GetTypeInfo();

        internal static readonly TypeInfo DynamicRowEnumerableType = typeof(DynamicRowEnumerable<>).GetTypeInfo();
        internal static readonly TypeInfo DynamicRowEnumerableNonGenericType = typeof(DynamicRowEnumerableNonGeneric).GetTypeInfo();
        internal static readonly TypeInfo PassthroughRowEnumerableType = typeof(PassthroughRowEnumerable).GetTypeInfo();

        internal static readonly TypeInfo DefaultTypeDescriberType = typeof(DefaultTypeDescriber).GetTypeInfo();

        internal static readonly TypeInfo ThrowType = typeof(Throw).GetTypeInfo();

        internal static readonly TypeInfo TupleDynamicParsersType = typeof(TupleDynamicParsers<>).GetTypeInfo();

        internal static readonly TypeInfo ITypeDescriberType = typeof(ITypeDescriber).GetTypeInfo();
    }
}

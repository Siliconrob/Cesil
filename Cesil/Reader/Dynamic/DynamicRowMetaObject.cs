﻿using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;

namespace Cesil
{
    internal sealed class DynamicRowMetaObject : DynamicMetaObject
    {
        private readonly DynamicRow Row;

        internal DynamicRowMetaObject(DynamicRow outer, Expression exp) : base(exp, BindingRestrictions.Empty, outer)
        {
            Row = outer;
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        => new DynamicRowMemberNameEnumerable(Row);

        public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
        {
            var expressionIsDynamicRowRestriction = BindingRestrictions.GetTypeRestriction(Expression, Types.DynamicRow);

            // only supported operation is .Dispose()
            if (binder.Name == nameof(DynamicRow.Dispose) && args.Length == 0)
            {
                var castToRow = Expression.Convert(Expression, Types.DynamicRow);
                var callDispose = Expression.Call(castToRow, Methods.DynamicRow.Dispose);

                Expression final;

                if (binder.ReturnType == Types.Void)
                {
                    final = callDispose;
                }
                else
                {
                    if (binder.ReturnType == Types.Object)
                    {
                        final = Expression.Block(callDispose, Expressions.Constant_Null);
                    }
                    else
                    {
                        final = Expression.Block(callDispose, Expression.Default(binder.ReturnType));
                    }
                }

                // we can cache this forever (for this type), doesn't vary by anything else
                return new DynamicMetaObject(final, expressionIsDynamicRowRestriction);
            }

            var msg = Expression.Constant($"Only the Dispose() method is supported.");
            var invalidOpCall = Methods.Throw.InvalidOperationExceptionOfObject;
            var call = Expression.Call(invalidOpCall, msg);

            // we can cache this forever (for this type), since there's no scenario under which a non-Dispose call
            //    becomes legal
            return new DynamicMetaObject(call, expressionIsDynamicRowRestriction);
        }

        public override DynamicMetaObject BindGetIndex(GetIndexBinder binder, DynamicMetaObject[] indexes)
        {
            var expressionIsDynamicRowRestriction = BindingRestrictions.GetTypeRestriction(Expression, Types.DynamicRow);

            if (indexes.Length != 1)
            {
                var msg = Expression.Constant($"Only single indexers are supported.");
                var invalidOpCall = Methods.Throw.InvalidOperationExceptionOfObject;
                var call = Expression.Call(invalidOpCall, msg);

                // we can cache this forever (for this type), since there's no scenario under which indexes != 1 becomes correct
                return new DynamicMetaObject(call, expressionIsDynamicRowRestriction);
            }

            var indexExp = indexes[0].Expression;
            var indexType = indexes[0].RuntimeType.GetTypeInfo();

            if (indexType == Types.Int)
            {
                var indexExpressionIsIntRestriction = BindingRestrictions.GetTypeRestriction(indexExp, Types.Int);

                var castToRow = Expression.Convert(Expression, Types.DynamicRow);

                var index = Expression.Convert(indexExp, Types.Int);
                var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetAt, index);

                var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

                var block = Expression.Block(assertNotDisposed, callOnSelf);

                var finalRestrictions = expressionIsDynamicRowRestriction.Merge(indexExpressionIsIntRestriction);
                return new DynamicMetaObject(block, finalRestrictions);
            }

            if (indexType == Types.String)
            {
                var indexExpressionIsStringRestriction = BindingRestrictions.GetTypeRestriction(indexExp, Types.String);

                var castToRow = Expression.Convert(Expression, Types.DynamicRow);

                var col = Expression.Convert(indexExp, Types.String);
                var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetByName, col);

                var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

                var block = Expression.Block(assertNotDisposed, callOnSelf);

                var finalRestrictions = expressionIsDynamicRowRestriction.Merge(indexExpressionIsStringRestriction);
                return new DynamicMetaObject(block, finalRestrictions);
            }

            if (indexType == Types.Index)
            {
                var indexExpressionIsIndexRestriction = BindingRestrictions.GetTypeRestriction(indexExp, Types.Index);

                var castToRow = Expression.Convert(Expression, Types.DynamicRow);

                var col = Expression.Convert(indexExp, Types.Index);
                var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetByIndex, col);

                var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

                var block = Expression.Block(assertNotDisposed, callOnSelf);

                var finalRestrictions = expressionIsDynamicRowRestriction.Merge(indexExpressionIsIndexRestriction);
                return new DynamicMetaObject(block, finalRestrictions);
            }

            if (indexType == Types.Range)
            {
                var indexExpressionIsRangeRestriction = BindingRestrictions.GetTypeRestriction(indexExp, Types.Range);

                var castToRow = Expression.Convert(Expression, Types.DynamicRow);

                var range = Expression.Convert(indexExp, Types.Range);
                var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetRange, range);

                var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

                var block = Expression.Block(assertNotDisposed, callOnSelf);

                var finalRestrictions = expressionIsDynamicRowRestriction.Merge(indexExpressionIsRangeRestriction);
                return new DynamicMetaObject(block, finalRestrictions);
            }

            if (indexType == Types.ColumnIdentifier)
            {
                var indexExpressionIsRangeRestriction = BindingRestrictions.GetTypeRestriction(indexExp, Types.ColumnIdentifier);

                var castToRow = Expression.Convert(Expression, Types.DynamicRow);

                var colId = Expression.Convert(indexExp, Types.ColumnIdentifier);
                var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetByIdentifier, colId);

                var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

                var block = Expression.Block(assertNotDisposed, callOnSelf);

                var finalRestrictions = expressionIsDynamicRowRestriction.Merge(indexExpressionIsRangeRestriction);
                return new DynamicMetaObject(block, finalRestrictions);
            }

            // no binder
            {
                var msg = Expression.Constant($"Only string, int, Index, and Range indexers are supported.");
                var invalidOpCall = Methods.Throw.InvalidOperationExceptionOfObject;
                var call = Expression.Call(invalidOpCall, msg);

                // we can cache this forever (for this type), since there's no scenario under which incorrect index types become correct
                return new DynamicMetaObject(call, expressionIsDynamicRowRestriction);
            }
        }

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            var restrictions = BindingRestrictions.GetTypeRestriction(Expression, Types.DynamicRow);

            var name = Expression.Constant(binder.Name);
            var castToRow = Expression.Convert(Expression, Types.DynamicRow);
            var callOnSelf = Expression.Call(castToRow, Methods.DynamicRow.GetByName, name);

            var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);

            var block = Expression.Block(assertNotDisposed, callOnSelf);

            return new DynamicMetaObject(block, restrictions);
        }

        private BindingRestrictions MakeRestrictions(DynamicRowConverter? expected, TypeInfo returnType)
        {
            var expectedConst = Expression.Constant(expected);
            var get = GetRowConverter(returnType);
            var eq = Expression.Equal(expectedConst, get);

            var sameConverterRestriction = BindingRestrictions.GetExpressionRestriction(eq);

            var expressionIsRowRestriction = BindingRestrictions.GetTypeRestriction(Expression, Types.DynamicRow);

            var ret = expressionIsRowRestriction.Merge(sameConverterRestriction);

            return ret;
        }

        private Expression GetRowConverter(TypeInfo forType)
        {
            var typeConst = Expression.Constant(forType);
            var selfAsRow = Expression.Convert(Expression, Types.DynamicRow);
            var converter = Expression.Field(selfAsRow, Fields.DynamicRow.Converter);
            var rowNumber = Expression.Field(selfAsRow, Fields.DynamicRow.RowNumber);
            var columns = Expression.Field(selfAsRow, Fields.DynamicRow.Columns);
            var context = Expression.Field(selfAsRow, Fields.DynamicRow.Context);
            var owner = Expression.Field(selfAsRow, Fields.DynamicRow.Owner);
            var options = Expression.Call(owner, Methods.IDynamicRowOwner.Options);

            var getCtx = Expression.Call(Methods.ReadContext.ConvertingRow, options, rowNumber, context);

            var dynamicRowConverter = Expression.Call(converter, Methods.ITypeDescriber.GetDynamicRowConverter, getCtx, columns, typeConst);

            return dynamicRowConverter;
        }

        public override DynamicMetaObject BindConvert(ConvertBinder binder)
        {
            var retType = binder.ReturnType.GetTypeInfo();

            // special case, converting to IDisposable will ALWAYS succeed
            //   because every dynamic row supports disposal
            if (retType == Types.IDisposable)
            {
                var alwaysRestrictions = BindingRestrictions.GetTypeRestriction(Expression, Types.DynamicRow);
                var cast = Expression.Convert(Expression, Types.IDisposable);

                // intentionally NOT checking if the row is already disposed

                return new DynamicMetaObject(cast, alwaysRestrictions);
            }

            var converterInterface = Row.Converter;
            var index = Row.RowNumber;

            var ctx = ReadContext.ConvertingRow(Row.Owner.Options, index, Row.Context);

            var converter = converterInterface.GetDynamicRowConverter(in ctx, Row.Columns, retType);

            var restrictions = MakeRestrictions(converter, retType);

            if (converter == null)
            {
                var invalidOpCall = Methods.Throw.InvalidOperationException.MakeGenericMethod(retType);
                var throwMsg = Expression.Call(invalidOpCall, Expression.Constant($"No row converter discovered for {retType}"));

                return new DynamicMetaObject(throwMsg, restrictions);
            }

            if (!binder.ReturnType.IsAssignableFrom(converter.TargetType))
            {
                var invalidOpCall = Methods.Throw.InvalidOperationException.MakeGenericMethod(retType);
                var throwMsg = Expression.Call(invalidOpCall, Expression.Constant($"Row converter {converter} does not create a type assignable to {binder.ReturnType}, returns {converter.TargetType}"));

                return new DynamicMetaObject(throwMsg, restrictions);
            }

            var statements = new List<Expression>();

            var assertNotDisposed = MakeAssertNotDisposedExpression(Expression);
            statements.Add(assertNotDisposed);

            var selfAsRow = Expression.Convert(Expression, Types.DynamicRow);
            var dynRowVar = Expressions.Variable_DynamicRow;
            var assignDynRow = Expression.Assign(dynRowVar, selfAsRow);
            statements.Add(assignDynRow);

            var outArg = Expression.Variable(binder.ReturnType);

            var callGetContext = Expression.Call(dynRowVar, Methods.DynamicRow.GetReadContext);
            var readCtxVar = Expressions.Variable_ReadContext;
            var assignReadCtx = Expression.Assign(readCtxVar, callGetContext);
            statements.Add(assignReadCtx);

            var convertExp = converter.MakeExpression(binder.ReturnType.GetTypeInfo(), dynRowVar, readCtxVar, outArg);

            var errorMsg = Expression.Constant($"{nameof(DynamicRowConverter)} ({converter}) could not convert dynamic row to {binder.ReturnType}");
            var throwInvalidOp = Expression.Call(Methods.Throw.InvalidOperationExceptionOfObject, errorMsg);

            var ifFalseThrow = Expression.IfThen(Expression.Not(convertExp), throwInvalidOp);
            statements.Add(ifFalseThrow);
            statements.Add(outArg);

            var block = Expression.Block(new[] { outArg, dynRowVar, readCtxVar }, statements);

            return new DynamicMetaObject(block, restrictions);
        }

        private static Expression MakeAssertNotDisposedExpression(Expression exp)
        {
            var cast = Expression.Convert(exp, Types.ITestableDisposable);
            var call = Expression.Call(Methods.DisposableHelper.AssertNotDisposed, cast);

            return call;
        }
    }
}

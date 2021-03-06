﻿namespace Cesil
{
    internal readonly struct ShallowReadContext
    {
        internal readonly ReadContextMode Mode;
        internal readonly int RowNumber;
        internal readonly int ColumnIndex;

        internal ShallowReadContext(in ReadContext ctx)
        {
            Mode = ctx.Mode;
            RowNumber = ctx.RowNumber;
            ColumnIndex = ctx.HasColumn ? ctx.Column.Index : -1;
        }
    }
}

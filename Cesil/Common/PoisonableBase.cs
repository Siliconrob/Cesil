﻿using System;
using System.Runtime.CompilerServices;

namespace Cesil
{
    internal abstract class PoisonableBase
    {
        internal PoisonType? Poison;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void SetPoison(Exception e)
        {
            if(e is AggregateException ae)
            {
                var inner = ae.InnerException;
                if(inner != null)
                {
                    SetPoison(inner);
                    return;
                }

                if(ae.InnerExceptions.Count > 0)
                {
                    SetPoison(ae.InnerExceptions[0]);
                    return;
                }

                Poison = PoisonType.Exception;
                return;
            }

            if(e is OperationCanceledException)
            {
                Poison = PoisonType.Cancelled;
                return;
            }

            Poison = PoisonType.Exception;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void AssertNotPoisoned()
        {
            if (Poison != null)
            {
                switch (Poison.Value)
                {
                    case PoisonType.Cancelled: Throw.InvalidOperationException<object>("Object is in an invalid state, a previous operation was cancelled"); return;
                    case PoisonType.Exception: Throw.InvalidOperationException<object>("Object is in an invalid state, a previous operation raised an exception"); return;
                    default:
                        Throw.Exception<object>($"Unexpected {nameof(PoisonType)}: {Poison}");
                        return;
                }
            }
        }
    }
}
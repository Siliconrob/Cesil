﻿using System;
using System.Reflection;

namespace Cesil
{
    /// <summary>
    /// Delegate used to create InstanceProviders.
    /// </summary>
    public delegate bool InstanceProviderDelegate<TInstance>(out TInstance instance);

    /// <summary>
    /// Represents a way to create new instances of a type.
    /// 
    /// This can be backed by a zero-parameter constructor, a static 
    ///   method, or a delegate.
    /// </summary>
    public sealed class InstanceProvider : IEquatable<InstanceProvider>
    {
        internal BackingMode Mode
        {
            get
            {
                if (Constructor.HasValue) return BackingMode.Constructor;
                if (Delegate.HasValue) return BackingMode.Delegate;
                if (Method.HasValue) return BackingMode.Method;

                return BackingMode.None;
            }
        }

        internal readonly TypeInfo ConstructsType;

        internal readonly NonNull<ConstructorInfo> Constructor;

        internal readonly NonNull<Delegate> Delegate;

        internal readonly NonNull<MethodInfo> Method;

        internal InstanceProvider(ConstructorInfo cons)
        {
            Constructor.Value = cons;
            ConstructsType = cons.DeclaringTypeNonNull();
        }

        internal InstanceProvider(Delegate del, TypeInfo forType)
        {
            Delegate.Value = del;
            ConstructsType = forType;
        }

        internal InstanceProvider(MethodInfo mtd, TypeInfo forType)
        {
            Method.Value = mtd;
            ConstructsType = forType;
        }

        /// <summary>
        /// Creates a new InstanceProvider from a method.
        /// 
        /// The method must:
        ///   - be static
        ///   - return a bool
        ///   - have a single out parameter of the constructed type
        /// </summary>
        public static InstanceProvider ForMethod(MethodInfo method)
        {
            Utils.CheckArgumentNull(method, nameof(method));

            if (!method.IsStatic)
            {
                return Throw.ArgumentException<InstanceProvider>("Method must be static", nameof(method));
            }

            if (method.ReturnType.GetTypeInfo() != Types.BoolType)
            {
                return Throw.ArgumentException<InstanceProvider>("Method must return a boolean", nameof(method));
            }

            var ps = method.GetParameters();
            if (ps.Length != 1)
            {
                return Throw.ArgumentException<InstanceProvider>("Method must have a single out parameter", nameof(method));
            }

            var outP = ps[0].ParameterType.GetTypeInfo();
            if (!outP.IsByRef)
            {
                return Throw.ArgumentException<InstanceProvider>("Method must have a single out parameter, parameter was not by ref", nameof(method));
            }

            var constructs = outP.GetElementTypeNonNull();

            return new InstanceProvider(method, constructs);
        }

        /// <summary>
        /// Create a new InstanceProvider from a parameterless constructor.
        /// 
        /// The constructed type must be concrete, that is:
        ///   - not an interface
        ///   - not an abstract class
        ///   - not a generic parameter
        ///   - not an unbound generic type (ie. a generic type definition)
        /// </summary>
        public static InstanceProvider ForParameterlessConstructor(ConstructorInfo constructor)
        {
            Utils.CheckArgumentNull(constructor, nameof(constructor));

            var ps = constructor.GetParameters();
            if (ps.Length != 0)
            {
                return Throw.ArgumentException<InstanceProvider>("Constructor must take 0 parameters", nameof(constructor));
            }

            var t = constructor.DeclaringTypeNonNull();
            if (t.IsInterface)
            {
                return Throw.ArgumentException<InstanceProvider>("Constructed type must be concrete, found an interface", nameof(constructor));
            }

            if (t.IsAbstract)
            {
                return Throw.ArgumentException<InstanceProvider>("Constructed type must be concrete, found an abstract class", nameof(constructor));
            }

            if (t.IsGenericTypeParameter)
            {
                return Throw.ArgumentException<InstanceProvider>("Constructed type must be concrete, found a generic parameter", nameof(constructor));
            }

            if (t.IsGenericTypeDefinition)
            {
                return Throw.ArgumentException<InstanceProvider>("Constructed type must be concrete, found a generic type definition", nameof(constructor));
            }

            return new InstanceProvider(constructor);
        }

        /// <summary>
        /// Create a new InstanceProvider from delegate.
        /// 
        /// There are no restrictions on what the give delegate may do,
        ///   but be aware that it may be called from many different contexts.
        /// </summary>
        public static InstanceProvider ForDelegate<TInstance>(InstanceProviderDelegate<TInstance> del)
        {
            Utils.CheckArgumentNull(del, nameof(del));

            return new InstanceProvider(del, typeof(TInstance).GetTypeInfo());
        }

        /// <summary>
        /// Returns true if this object equals the given InstanceProvider.
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is InstanceProvider i)
            {
                return Equals(i);
            }

            return false;
        }

        /// <summary>
        /// Returns true if this object equals the given InstanceProvider.
        /// </summary>
        public bool Equals(InstanceProvider instanceProvider)
        {
            if (ReferenceEquals(instanceProvider, null)) return false;

            if (Mode != instanceProvider.Mode) return false;

            if (ConstructsType != instanceProvider.ConstructsType) return false;

            switch (Mode)
            {
                case BackingMode.Constructor: return instanceProvider.Constructor.Value == Constructor.Value;
                case BackingMode.Delegate: return instanceProvider.Delegate.Value == Delegate.Value;
                case BackingMode.Method: return instanceProvider.Method.Value == Method.Value;
                default: return Throw.InvalidOperationException<bool>($"Unexpected {nameof(BackingMode)}: {Mode}");
            }
        }

        /// <summary>
        /// Returns a stable hash for this InstanceProvider.
        /// </summary>
        public override int GetHashCode()
        => HashCode.Combine(nameof(InstanceProvider), Constructor, ConstructsType, Delegate, Method, Mode);

        /// <summary>
        /// Returns a representation of this InstanceProvider object.
        /// 
        /// Only for debugging, this value is not guaranteed to be stable.
        /// </summary>
        public override string ToString()
        {
            switch (Mode)
            {
                case BackingMode.Constructor:
                    return $"{nameof(InstanceProvider)} using parameterless constructor {Constructor} to create {ConstructsType}";
                case BackingMode.Delegate:
                    return $"{nameof(InstanceProvider)} using delegate {Delegate} to create {ConstructsType}";
                case BackingMode.Method:
                    return $"{nameof(InstanceProvider)} using method {Method} to create {ConstructsType}";
                default:
                    return Throw.InvalidOperationException<string>($"Unexpected {nameof(BackingMode)}: {Mode}");
            }
        }

        /// <summary>
        /// Convenience operator, equivalent to calling ForMethod if non-null.
        /// 
        /// Returns null if mtd is null.
        /// </summary>
        public static explicit operator InstanceProvider?(MethodInfo? mtd)
        => mtd == null ? null : ForMethod(mtd);

        /// <summary>
        /// Convenience operator, equivalent to calling ForParameterlessConstructor if non-null.
        /// 
        /// Returns null if field is null.
        /// </summary>
        public static explicit operator InstanceProvider?(ConstructorInfo? cons)
        => cons == null ? null : ForParameterlessConstructor(cons);

        /// <summary>
        /// Convenience operator, equivalent to calling ForDelegate if non-null.
        /// 
        /// Returns null if del is null.
        /// </summary>
        public static explicit operator InstanceProvider?(Delegate? del)
        {
            if (del == null) return null;

            var delType = del.GetType().GetTypeInfo();
            if (delType.IsGenericType)
            {
                var delGenType = delType.GetGenericTypeDefinition().GetTypeInfo();
                if (delGenType == Types.InstanceProviderDelegateType)
                {
                    var genArgs = delType.GetGenericArguments();
                    var makes = genArgs[0].GetTypeInfo();

                    return new InstanceProvider(del, makes);
                }
            }

            var mtd = del.Method;
            var ret = mtd.ReturnType.GetTypeInfo();
            if (ret != Types.BoolType)
            {
                return Throw.InvalidOperationException<InstanceProvider>($"Delegate must return boolean, found {ret}");
            }

            var ps = mtd.GetParameters();
            if (ps.Length != 1)
            {
                return Throw.InvalidOperationException<InstanceProvider>($"Delegate must have a single out parameter");
            }

            var outP = ps[0].ParameterType.GetTypeInfo();
            if (!outP.IsByRef)
            {
                return Throw.InvalidOperationException<InstanceProvider>("Delegate must have a single out parameter, parameter was not by ref");
            }

            var constructs = outP.GetElementTypeNonNull();

            var instanceBuilderDel = Types.InstanceProviderDelegateType.MakeGenericType(constructs);
            var invoke = del.GetType().GetTypeInfo().GetMethodNonNull("Invoke");

            var reboundDel = System.Delegate.CreateDelegate(instanceBuilderDel, del, invoke);

            return new InstanceProvider(reboundDel, constructs);
        }

        /// <summary>
        /// Compare two InstanceProviders for equality
        /// </summary>
        public static bool operator ==(InstanceProvider? a, InstanceProvider? b)
        => Utils.NullReferenceEquality(a, b);

        /// <summary>
        /// Compare two InstanceProvider for inequality
        /// </summary>
        public static bool operator !=(InstanceProvider? a, InstanceProvider? b)
        => !(a == b);
    }
}

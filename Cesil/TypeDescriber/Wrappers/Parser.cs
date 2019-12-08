﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cesil
{
    /// <summary>
    /// Delegate type for parsers.
    /// </summary>
    public delegate bool ParserDelegate<TOutput>(ReadOnlySpan<char> data, in ReadContext context, out TOutput result);

    /// <summary>
    /// Represents code used to parse values into concrete types.
    /// 
    /// Wraps either a MethodInfo, a ParserDelegate, or a ConstructorInfo.
    /// </summary>
    public sealed class Parser : IEquatable<Parser>
    {
        // internal for testing purposes
        internal static readonly IReadOnlyDictionary<TypeInfo, Parser> TypeParsers;

        static Parser()
        {
            // load up default parsers
            var ret = new Dictionary<TypeInfo, Parser>();
            foreach (var mtd in Types.DefaultTypeParsersType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var thirdArg = mtd.GetParameters()[2];
                var forType = thirdArg.ParameterType.GetTypeInfo().GetElementTypeNonNull();

                var parser = ForMethod(mtd);

                ret.Add(forType, parser);
            }

            TypeParsers = ret;
        }

        internal BackingMode Mode
        {
            get
            {
                if (Method.HasValue) return BackingMode.Method;
                if (Delegate.HasValue) return BackingMode.Delegate;
                if (Constructor.HasValue) return BackingMode.Constructor;

                return BackingMode.None;
            }
        }

        internal readonly NonNull<MethodInfo> Method;

        internal readonly NonNull<Delegate> Delegate;

        internal readonly NonNull<ConstructorInfo> Constructor;

        internal readonly TypeInfo Creates;

        private Parser(MethodInfo method, TypeInfo creates)
        {
            Method.Value = method;
            Delegate.Clear();
            Constructor.Clear();
            Creates = creates;
        }

        private Parser(Delegate del, TypeInfo creates)
        {
            Delegate.Value = del;
            Method.Clear();
            Constructor.Clear();
            Creates = creates;
        }

        private Parser(ConstructorInfo cons)
        {
            Delegate.Clear();
            Method.Clear();
            Constructor.Value = cons;
            Creates = cons.DeclaringTypeNonNull();
        }

        /// <summary>
        /// Create a Parser from the given method.
        /// 
        /// The method must:
        ///  - be static
        ///  - return a bool
        ///  - have parameters
        ///     * ReadOnlySpan(char)
        ///     * in ReadContext, 
        ///     * out assignable to outputType
        /// </summary>
        public static Parser ForMethod(MethodInfo method)
        {
            Utils.CheckArgumentNull(method, nameof(method));

            // parser must
            //   be a static method
            //   take a ReadOnlySpan<char>
            //   take an in ReadContext
            //   have an out parameter of a type assignable to the parameter of setter
            //   and return a boolean
            if (!method.IsStatic)
            {
                return Throw.ArgumentException<Parser>($"{nameof(method)} be a static method", nameof(method));
            }

            var args = method.GetParameters();
            if (args.Length != 3)
            {
                return Throw.ArgumentException<Parser>($"{nameof(method)} must have three parameters", nameof(method));
            }

            var p1 = args[0].ParameterType.GetTypeInfo();
            var p2 = args[1].ParameterType.GetTypeInfo();
            var p3 = args[2].ParameterType.GetTypeInfo();

            if (p1 != Types.ReadOnlySpanOfCharType)
            {
                return Throw.ArgumentException<Parser>($"The first parameter of {nameof(method)} must be a {nameof(ReadOnlySpan<char>)}", nameof(method));
            }

            if (!p2.IsByRef)
            {
                return Throw.ArgumentException<Parser>($"The second parameter of {nameof(method)} must be an in", nameof(method));
            }

            var p2Elem = p2.GetElementTypeNonNull();
            if (p2Elem != Types.ReadContextType)
            {
                return Throw.ArgumentException<Parser>($"The second parameter of {nameof(method)} must be a {nameof(ReadContext)}", nameof(method));
            }

            if (!p3.IsByRef)
            {
                return Throw.ArgumentException<Parser>($"The third parameter of {nameof(method)} must be an out", nameof(method));
            }

            var underlying = p3.GetElementTypeNonNull();

            var parserRetType = method.ReturnType.GetTypeInfo();
            if (parserRetType != Types.BoolType)
            {
                return Throw.ArgumentException<Parser>($"{nameof(method)} must must return a bool", nameof(method));
            }

            return new Parser(method, underlying);
        }

        /// <summary>
        /// Create a Parser from the given constructor.
        /// 
        /// The method must:
        ///  - take either a ReadOnlySpan(char)
        /// or
        ///  - take parameters
        ///     * ReadOnlySpan(char)
        ///     * in ReadContext
        /// </summary>
        public static Parser ForConstructor(ConstructorInfo constructor)
        {
            Utils.CheckArgumentNull(constructor, nameof(constructor));

            var ps = constructor.GetParameters();
            if (ps.Length == 1)
            {
                var firstP = ps[0].ParameterType.GetTypeInfo();

                if (firstP != Types.ReadOnlySpanOfCharType)
                {
                    return Throw.ArgumentException<Parser>($"{nameof(constructor)} first parameter must be a ReadOnlySpan<char>", nameof(constructor));
                }
            }
            else if (ps.Length == 2)
            {
                var firstP = ps[0].ParameterType.GetTypeInfo();

                if (firstP != Types.ReadOnlySpanOfCharType)
                {
                    return Throw.ArgumentException<Parser>($"{nameof(constructor)} first parameter must be a ReadOnlySpan<char>", nameof(constructor));
                }

                var secondP = ps[1].ParameterType.GetTypeInfo();
                if (!secondP.IsByRef)
                {
                    return Throw.ArgumentException<Parser>($"{nameof(constructor)} second parameter must be an in ReadContext, was not by ref", nameof(constructor));
                }

                var secondPElem = secondP.GetElementTypeNonNull();
                if (secondPElem != Types.ReadContextType)
                {
                    return Throw.ArgumentException<Parser>($"{nameof(constructor)} second parameter must be an in ReadContext, found {secondPElem}", nameof(constructor));
                }
            }
            else
            {
                return Throw.ArgumentException<Parser>($"{nameof(constructor)} must have one or two parameters", nameof(constructor));
            }

            return new Parser(constructor);
        }

        /// <summary>
        /// Create a Parser from the given delegate.
        /// </summary>
        public static Parser ForDelegate<TOutput>(ParserDelegate<TOutput> del)
        {
            Utils.CheckArgumentNull(del, nameof(del));

            return new Parser(del, typeof(TOutput).GetTypeInfo());
        }

        /// <summary>
        /// Returns the default parser for the given type, if any exists.
        /// </summary>
        [return: NullableExposed("May not be known, null is cleanest way to handle it")]
        public static Parser? GetDefault(TypeInfo forType)
        {
            Utils.CheckArgumentNull(forType, nameof(forType));

            if (forType.IsEnum)
            {
                if (forType.GetCustomAttribute<FlagsAttribute>() == null)
                {
                    var parsingClass = Types.DefaultEnumTypeParserType.MakeGenericType(forType).GetTypeInfo();
                    var parserField = parsingClass.GetFieldNonNull(nameof(DefaultTypeParsers.DefaultEnumTypeParser<StringComparison>.TryParseEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser?)parserField.GetValue(null);

                    return parser;
                }
                else
                {
                    var parsingClass = Types.DefaultFlagsEnumTypeParserType.MakeGenericType(forType).GetTypeInfo();
                    var parserField = parsingClass.GetFieldNonNull(nameof(DefaultTypeParsers.DefaultFlagsEnumTypeParser<StringComparison>.TryParseFlagsEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser?)parserField.GetValue(null);

                    return parser;
                }
            }

            var nullableElem = Nullable.GetUnderlyingType(forType)?.GetTypeInfo();
            if (nullableElem != null && nullableElem.IsEnum)
            {
                if (nullableElem.GetCustomAttribute<FlagsAttribute>() == null)
                {
                    var parsingClass = Types.DefaultEnumTypeParserType.MakeGenericType(nullableElem).GetTypeInfo();
                    var parserField = parsingClass.GetFieldNonNull(nameof(DefaultTypeParsers.DefaultEnumTypeParser<StringComparison>.TryParseNullableEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser?)parserField.GetValue(null);

                    return parser;
                }
                else
                {
                    var parsingClass = Types.DefaultFlagsEnumTypeParserType.MakeGenericType(nullableElem).GetTypeInfo();
                    var parserField = parsingClass.GetFieldNonNull(nameof(DefaultTypeParsers.DefaultFlagsEnumTypeParser<StringComparison>.TryParseNullableFlagsEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser?)parserField.GetValue(null);

                    return parser;
                }
            }

            if (!TypeParsers.TryGetValue(forType, out var ret))
            {
                return null;
            }

            return ret;
        }

        /// <summary>
        /// Describes this Parser.
        /// 
        /// This is provided for debugging purposes, and the format is not guaranteed to be stable between releases.
        /// </summary>
        public override string ToString()
        {
            switch (Mode)
            {
                case BackingMode.Method:
                    return $"{nameof(Parser)} backed by method {Method} creating {Creates}";
                case BackingMode.Delegate:
                    return $"{nameof(Parser)} backed by delegate {Delegate} creating {Creates}";
                case BackingMode.Constructor:
                    return $"{nameof(Parser)} backed by constructor {Constructor} creating {Creates}";
                default:
                    return Throw.InvalidOperationException<string>($"Unexpected {nameof(BackingMode)}: {Mode}");
            }
        }

        /// <summary>
        /// Returns true if the given Parser is equivalent to this one
        /// </summary>
        public bool Equals(Parser parser)
        {
            if (ReferenceEquals(parser, null)) return false;

            var selfMode = Mode;
            var otherMode = parser.Mode;

            if (selfMode != otherMode) return false;

            switch (selfMode)
            {
                case BackingMode.Constructor:
                    return Constructor.Value == parser.Constructor.Value;
                case BackingMode.Delegate:
                    return Delegate.Value == parser.Delegate.Value;
                case BackingMode.Method:
                    return Method.Value == parser.Method.Value;
                default:
                    return Throw.InvalidOperationException<bool>($"Unexpected {nameof(BackingMode)}: {selfMode}");
            }
        }

        /// <summary>
        /// Returns true if the given object is equivalent to this one
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is Parser p)
            {
                return Equals(p);
            }

            return false;
        }

        /// <summary>
        /// Returns a stable hash for this Parser.
        /// </summary>
        public override int GetHashCode()
        => HashCode.Combine(nameof(Parser), Mode, Method, Constructor, Delegate);

        /// <summary>
        /// Compare two Parsers for equality
        /// </summary>
        public static bool operator ==(Parser? a, Parser? b)
        => Utils.NullReferenceEquality(a, b);

        /// <summary>
        /// Compare two Parsers for inequality
        /// </summary>
        public static bool operator !=(Parser? a, Parser? b)
        => !(a == b);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForMethod if non-null.
        /// 
        /// Returns null if mtd is null.
        /// </summary>
        public static explicit operator Parser?(MethodInfo? mtd)
        => mtd == null ? null : ForMethod(mtd);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForConstructor if non-null.
        /// 
        /// Returns null if cons is null.
        /// </summary>
        public static explicit operator Parser?(ConstructorInfo? cons)
        => cons == null ? null : ForConstructor(cons);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForDelegate if non-null.
        /// 
        /// Returns null if del is null.
        /// </summary>
        public static explicit operator Parser?(Delegate? del)
        {
            if (del == null) return null;

            var delType = del.GetType().GetTypeInfo();

            if (delType.IsGenericType && delType.GetGenericTypeDefinition() == Types.ParserDelegateType)
            {
                var t = delType.GetGenericArguments()[0].GetTypeInfo();

                return new Parser(del, t);
            }

            var mtd = del.Method;
            var ret = mtd.ReturnType.GetTypeInfo();
            if (ret != Types.BoolType)
            {
                return Throw.InvalidOperationException<Parser>($"Delegate must return a bool");
            }

            var args = mtd.GetParameters();
            if (args.Length != 3)
            {
                return Throw.InvalidOperationException<Parser>($"Delegate must take 3 parameters");
            }

            var p1 = args[0].ParameterType.GetTypeInfo();
            if (p1 != Types.ReadOnlySpanOfCharType)
            {
                return Throw.InvalidOperationException<Parser>($"The first paramater to the delegate must be a {nameof(ReadOnlySpan<char>)}");
            }

            var p2 = args[1].ParameterType.GetTypeInfo();
            if (!p2.IsByRef)
            {
                return Throw.InvalidOperationException<Parser>($"The second paramater to the delegate must be an in {nameof(ReadContext)}, was not by ref");
            }

            if (p2.GetElementTypeNonNull() != Types.ReadContextType)
            {
                return Throw.InvalidOperationException<Parser>($"The second paramater to the delegate must be an in {nameof(ReadContext)}");
            }

            var createsRef = args[2].ParameterType.GetTypeInfo();
            if (!createsRef.IsByRef)
            {
                return Throw.InvalidOperationException<Parser>($"The third paramater to the delegate must be an out type, was not by ref");
            }

            var creates = createsRef.GetElementTypeNonNull();

            var parserDel = Types.ParserDelegateType.MakeGenericType(creates);
            var invoke = del.GetType().GetTypeInfo().GetMethodNonNull("Invoke");

            var reboundDel = System.Delegate.CreateDelegate(parserDel, del, invoke);

            return new Parser(reboundDel, creates);
        }
    }
}

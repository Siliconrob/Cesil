﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cesil
{
    /// <summary>
    /// Delegate type for parsers.
    /// </summary>
    public delegate bool ParserDelegate<T>(ReadOnlySpan<char> data, in ReadContext ctx, out T result);

    /// <summary>
    /// Represents code used to parse values into concrete types.
    /// 
    /// Wraps either a MethodInfo, a ParserDelegate, or a ConstructorInfo.
    /// </summary>
    public sealed class Parser : IEquatable<Parser>
    {
        private static readonly IReadOnlyDictionary<TypeInfo, Parser> TypeParsers;

        static Parser()
        {
            // load up default parsers
            var ret = new Dictionary<TypeInfo, Parser>();
            foreach (var mtd in Types.DefaultTypeParsersType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var thirdArg = mtd.GetParameters()[2];
                var forType = thirdArg.ParameterType.GetElementType().GetTypeInfo();

                var parser = ForMethod(mtd);

                ret.Add(forType, parser);
            }

            TypeParsers = ret;
        }

        internal BackingMode Mode
        {
            get
            {
                if (Method != null) return BackingMode.Method;
                if (Delegate != null) return BackingMode.Delegate;
                if (Constructor != null) return BackingMode.Constructor;

                return BackingMode.None;
            }
        }

        internal MethodInfo Method { get; }
        internal Delegate Delegate { get; }
        internal ConstructorInfo Constructor { get; }

        internal TypeInfo Creates { get; }

        private Parser(MethodInfo method, TypeInfo creates)
        {
            Method = method;
            Delegate = null;
            Constructor = null;
            Creates = creates;
        }

        private Parser(Delegate del, TypeInfo creates)
        {
            Delegate = del;
            Method = null;
            Constructor = null;
            Creates = creates;
        }

        private Parser(ConstructorInfo cons)
        {
            Delegate = null;
            Method = null;
            Constructor = cons;
            Creates = cons.DeclaringType.GetTypeInfo();
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
        public static Parser ForMethod(MethodInfo parser)
        {
            if (parser == null)
            {
                Throw.ArgumentNullException(nameof(parser));
            }

            // parser must
            //   be a static method
            //   take a ReadOnlySpan<char>
            //   take an in ReadContext
            //   have an out parameter of a type assignable to the parameter of setter
            //   and return a boolean
            if (!parser.IsStatic)
            {
                Throw.ArgumentException($"{nameof(parser)} be a static method", nameof(parser));
            }

            var args = parser.GetParameters();
            if (args.Length != 3)
            {
                Throw.ArgumentException($"{nameof(parser)} must have three parameters", nameof(parser));
            }

            var p1 = args[0].ParameterType.GetTypeInfo();
            var p2 = args[1].ParameterType.GetTypeInfo();
            var p3 = args[2].ParameterType.GetTypeInfo();

            if (p1 != Types.ReadOnlySpanOfCharType)
            {
                Throw.ArgumentException($"The first parameter of {nameof(parser)} must be a {nameof(ReadOnlySpan<char>)}", nameof(parser));
            }

            if (!p2.IsByRef)
            {
                Throw.ArgumentException($"The second parameter of {nameof(parser)} must be an in", nameof(parser));
            }

            var p2Elem = p2.GetElementType().GetTypeInfo();
            if (p2Elem != Types.ReadContextType)
            {
                Throw.ArgumentException($"The second parameter of {nameof(parser)} must be a {nameof(ReadContext)}", nameof(parser));
            }

            if (!p3.IsByRef)
            {
                Throw.ArgumentException($"The third parameter of {nameof(parser)} must be an out", nameof(parser));
            }

            var underlying = p3.GetElementType().GetTypeInfo();

            var parserRetType = parser.ReturnType.GetTypeInfo();
            if (parserRetType != Types.BoolType)
            {
                Throw.ArgumentException($"{nameof(parser)} must must return a bool", nameof(parser));
            }

            return new Parser(parser, underlying);
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
        public static Parser ForConstructor(ConstructorInfo cons)
        {
            if (cons == null)
            {
                Throw.ArgumentNullException(nameof(cons));
            }

            var ps = cons.GetParameters();
            if (ps.Length == 1)
            {
                var firstP = ps[0].ParameterType.GetTypeInfo();

                if (firstP != Types.ReadOnlySpanOfCharType)
                {
                    Throw.ArgumentException($"{nameof(cons)} first parameter must be a ReadOnlySpan<char>", nameof(cons));
                }
            }
            else if (ps.Length == 2)
            {
                var firstP = ps[0].ParameterType.GetTypeInfo();

                if (firstP != Types.ReadOnlySpanOfCharType)
                {
                    Throw.ArgumentException($"{nameof(cons)} first parameter must be a ReadOnlySpan<char>", nameof(cons));
                }

                var secondP = ps[1].ParameterType.GetTypeInfo();
                if (!secondP.IsByRef)
                {
                    Throw.ArgumentException($"{nameof(cons)} second parameter must be an in ReadContext, was not by ref", nameof(cons));
                }

                var secondPElem = secondP.GetElementType().GetTypeInfo();
                if (secondPElem != Types.ReadContextType)
                {
                    Throw.ArgumentException($"{nameof(cons)} second parameter must be an in ReadContext, found {secondPElem}", nameof(cons));
                }
            }
            else
            {
                Throw.ArgumentException($"{nameof(cons)} must have one or two parameters", nameof(cons));
            }

            return new Parser(cons);
        }

        /// <summary>
        /// Create a Parser from the given delegate.
        /// </summary>
        public static Parser ForDelegate<T>(ParserDelegate<T> del)
        {
            if (del == null)
            {
                Throw.ArgumentNullException(nameof(del));
            }

            return new Parser(del, typeof(T).GetTypeInfo());
        }

        /// <summary>
        /// Returns the default parser for the given type, if any exists.
        /// </summary>
        public static Parser GetDefault(TypeInfo forType)
        {
            if (forType.IsEnum)
            {
                if (forType.GetCustomAttribute<FlagsAttribute>() == null)
                {
                    var parsingClass = Types.DefaultEnumTypeParserType.MakeGenericType(forType).GetTypeInfo();
                    var parserField = parsingClass.GetField(nameof(DefaultTypeParsers.DefaultEnumTypeParser<StringComparison>.TryParseEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser)parserField.GetValue(null);

                    return parser;
                }
                else
                {
                    var parsingClass = Types.DefaultFlagsEnumTypeParserType.MakeGenericType(forType).GetTypeInfo();
                    var parserField = parsingClass.GetField(nameof(DefaultTypeParsers.DefaultFlagsEnumTypeParser<StringComparison>.TryParseFlagsEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser)parserField.GetValue(null);

                    return parser;
                }
            }

            var nullableElem = Nullable.GetUnderlyingType(forType)?.GetTypeInfo();
            if (nullableElem != null && nullableElem.IsEnum)
            {
                if (nullableElem.GetCustomAttribute<FlagsAttribute>() == null)
                {
                    var parsingClass = Types.DefaultEnumTypeParserType.MakeGenericType(nullableElem).GetTypeInfo();
                    var parserField = parsingClass.GetField(nameof(DefaultTypeParsers.DefaultEnumTypeParser<StringComparison>.TryParseNullableEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser)parserField.GetValue(null);

                    return parser;
                }
                else
                {
                    var parsingClass = Types.DefaultFlagsEnumTypeParserType.MakeGenericType(nullableElem).GetTypeInfo();
                    var parserField = parsingClass.GetField(nameof(DefaultTypeParsers.DefaultFlagsEnumTypeParser<StringComparison>.TryParseNullableFlagsEnumParser), BindingFlags.Static | BindingFlags.NonPublic);
                    var parser = (Parser)parserField.GetValue(null);

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
                    Throw.InvalidOperationException($"Unexpected {nameof(BackingMode)}: {Mode}");
                    // just for control flow
                    return default;
            }
        }

        /// <summary>
        /// Returns true if the given Parser is equivalent to this one
        /// </summary>
        public bool Equals(Parser other)
        {
            if (other == null) return false;

            var selfMode = other.Mode;
            var otherMode = other.Mode;

            if (selfMode != otherMode) return false;

            switch (selfMode)
            {
                case BackingMode.Constructor:
                    return Constructor == other.Constructor;
                case BackingMode.Delegate:
                    return Delegate == other.Delegate;
                case BackingMode.Method:
                    return Method == other.Method;
                default:
                    Throw.InvalidOperationException($"Unexpected {nameof(BackingMode)}: {selfMode}");
                    // just for control flow
                    return default;
            }
        }

        /// <summary>
        /// Returns true if the given object is equivalent to this one
        /// </summary>
        public override bool Equals(object obj)
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
        public static bool operator ==(Parser a, Parser b)
        => Utils.NullReferenceEquality(a, b);

        /// <summary>
        /// Compare two Parsers for inequality
        /// </summary>
        public static bool operator !=(Parser a, Parser b)
        => !(a == b);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForMethod if non-null.
        /// 
        /// Returns null if mtd is null.
        /// </summary>
        public static explicit operator Parser(MethodInfo mtd)
        => mtd == null ? null : ForMethod(mtd);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForConstructor if non-null.
        /// 
        /// Returns null if cons is null.
        /// </summary>
        public static explicit operator Parser(ConstructorInfo cons)
        => cons == null ? null : ForConstructor(cons);

        /// <summary>
        /// Convenience operator, equivalent to calling Parser.ForDelegate if non-null.
        /// 
        /// Returns null if del is null.
        /// </summary>
        public static explicit operator Parser(Delegate del)
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
                Throw.InvalidOperationException($"Delegate must return a bool");
            }

            var args = mtd.GetParameters();
            if (args.Length != 3)
            {
                Throw.InvalidOperationException($"Delegate must take 3 parameters");
            }

            var p1 = args[0].ParameterType.GetTypeInfo();
            if (p1 != Types.ReadOnlySpanOfCharType)
            {
                Throw.InvalidOperationException($"The first paramater to the delegate must be a {nameof(ReadOnlySpan<char>)}");
            }

            var p2 = args[1].ParameterType.GetTypeInfo();
            if (!p2.IsByRef)
            {
                Throw.InvalidOperationException($"The second paramater to the delegate must be an in {nameof(ReadContext)}, was not by ref");
            }

            if (p2.GetElementType() != Types.ReadContextType)
            {
                Throw.InvalidOperationException($"The second paramater to the delegate must be an in {nameof(ReadContext)}");
            }

            var createsRef = args[2].ParameterType.GetTypeInfo();
            if (!createsRef.IsByRef)
            {
                Throw.InvalidOperationException($"The third paramater to the delegate must be an out type, was not by ref");
            }

            var creates = createsRef.GetElementType().GetTypeInfo();

            var parserDel = Types.ParserDelegateType.MakeGenericType(creates);
            var invoke = del.GetType().GetMethod("Invoke");

            var reboundDel = Delegate.CreateDelegate(parserDel, del, invoke);

            return new Parser(reboundDel, creates);
        }
    }
}

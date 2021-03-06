using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

using static Cesil.SourceGenerator.Tests.TestHelper;

namespace Cesil.SourceGenerator.Tests
{
    public class SerializerConfigurationFixture : IAsyncLifetime
    {
        internal IMethodSymbol Method { get; private set; }
        internal ITypeSymbol Type { get; private set; }

        public async Task InitializeAsync()
        {
            var gen = new SerializerGenerator();
            var (comp, diags) = await RunSourceGeneratorAsync(
@"
namespace Foo 
{   
    class Foo
    {
    }
}", gen);

            Assert.Empty(diags);

            Type = Utils.NonNull(comp.GetTypeByMetadataName("System.String"));
            Method = Type.GetMembers().OfType<IMethodSymbol>().First();
        }

        public Task DisposeAsync()
        => Task.CompletedTask;
    }

    public class SerializerConfigurationTests: IClassFixture<SerializerConfigurationFixture>
    {
        private readonly ITypeSymbol Type;
        private readonly IMethodSymbol Method;

        public SerializerConfigurationTests(SerializerConfigurationFixture fixture)
        {
            Type = fixture.Type;
            Method = fixture.Method;
        }

        [Fact]
        public async Task SimpleAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class WriteMe
    {
        [SerializerMember(FormatterType=typeof(WriteMe), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }
        [SerializerMember(FormatterType=typeof(WriteMe), FormatterMethodName=nameof(ForString))]
        public string Fizz;
        [SerializerMember(Name=""Hello"", FormatterType=typeof(WriteMe), FormatterMethodName=nameof(ForDateTime))]
        public DateTime SomeMtd() => DateTime.Now;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ForString(string val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ForDateTime(DateTime val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

            Assert.Empty(diags);

            var shouldBeWriteMe = Assert.Single(gen.ToGenerateFor);
            Assert.Equal("WriteMe", shouldBeWriteMe.Identifier.ValueText);

            var members = gen.Members.First().Value;

            // Bar
            {
                var bar = Assert.Single(members, x => x.Name == "Bar");
                Assert.True(bar.EmitDefaultValue);
                Assert.Equal("ForInt", (bar.Formatter.Method.DeclaringSyntaxReferences.Single().GetSyntax() as MethodDeclarationSyntax).Identifier.ValueText);
                Assert.Equal("Bar", bar.Getter.Property.Name);
                Assert.Null(bar.Order);
                Assert.Null(bar.ShouldSerialize);
            }

            // Fizz
            {
                var fizz = Assert.Single(members, x => x.Name == "Fizz");
                Assert.True(fizz.EmitDefaultValue);
                Assert.Equal("ForString", (fizz.Formatter.Method.DeclaringSyntaxReferences.Single().GetSyntax() as MethodDeclarationSyntax).Identifier.ValueText);
                Assert.Equal("Fizz", fizz.Getter.Field.Name);
                Assert.Null(fizz.Order);
                Assert.Null(fizz.ShouldSerialize);
            }

            // Hello
            {
                var hello = Assert.Single(members, x => x.Name == "Hello");
                Assert.True(hello.EmitDefaultValue);
                Assert.Equal("ForDateTime", (hello.Formatter.Method.DeclaringSyntaxReferences.Single().GetSyntax() as MethodDeclarationSyntax).Identifier.ValueText);
                Assert.Equal("SomeMtd", hello.Getter.Method.Name);
                Assert.Null(hello.Order);
                Assert.Null(hello.ShouldSerialize);
            }
        }

        [Fact]
        public async Task BadFormattersAsync()
        {
            // not paired (missing method)
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType=typeof(BadFormatters))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.FormatterBothMustBeSet, d);
                        Assert.Equal("        [SerializerMember(FormatterType=typeof(BadFormatters))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // not paired (missing type)
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.FormatterBothMustBeSet, d);
                        Assert.Equal("        [SerializerMember(FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // missing method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=""SomethingElse"")]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.CouldNotFindMethod, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=\"SomethingElse\")]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // ambiguous method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ForInt()
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MultipleMethodsFound, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // generic method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt<T>(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MethodCannotBeGeneric, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // private method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        private static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MethodNotPublicOrInternal, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // internal method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MethodNotStatic, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // non-bool return method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static string ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => "";
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MethodMustReturnBool, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // void return method
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static void ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer){ }
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.MethodMustReturnBool, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // wrong number of parameters
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt() 
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // first param not int
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(string val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // first param by ref
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(ref int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // second param not in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // second param not WriteContext
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in string ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // third param by ref
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, ref IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // third param not IBufferWriter<char>
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<int> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // third param not IBufferWriter<anything>
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadFormatters
    {
        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, string buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadFormatterParameters, d);
                        Assert.Equal("        [SerializerMember(FormatterType = typeof(BadFormatters), FormatterMethodName=nameof(ForInt))]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }
        }

        [Fact]
        public async Task BadShouldSerializeAsync()
        {
            // not paired (missing method)
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar()
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.ShouldSerializeBothMustBeSet, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // not paired (missing type)
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar()
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.ShouldSerializeBothMustBeSet, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, too many parameters
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(int a, int b, int c, int d)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_TooMany, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, one parameter by ref
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(ref BadShouldSerializes a)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, one parameter wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(int a)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two parameters first by ref
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(ref BadShouldSerializes a, in WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two parameters first wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(int a, in WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two parameters second not by in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(BadShouldSerializes a, WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two parameters second wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(BadShouldSerializes a, in object ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, too many parameters
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public bool ShouldSerializeBar(int a, int b)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_TooMany, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, first parameter not in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public bool ShouldSerializeBar(WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_InstanceOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, first parameter wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class BadShouldSerializes
    {
        [SerializerMember(
            FormatterType=typeof(BadShouldSerializes),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadShouldSerializes),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar)
        )]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public bool ShouldSerializeBar(in object ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadShouldSerializeParameters_InstanceOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadShouldSerializes),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadShouldSerializes),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar)\r\n        )]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }
        }

        [Fact]
        public async Task BadEmitDefaultValueAsync()
        {
            // multiple declarations
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadEmitDefaultValues
    {
        [SerializerMember(
            FormatterType=typeof(BadEmitDefaultValues),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadEmitDefaultValues),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar),

            EmitDefaultValue = EmitDefaultValue.No
        )]
        [DataMember(EmitDefaultValue = false)]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(BadEmitDefaultValues row, in WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.EmitDefaultValueSpecifiedMultipleTimes, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadEmitDefaultValues),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadEmitDefaultValues),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar),\r\n\r\n            EmitDefaultValue = EmitDefaultValue.No\r\n        )]\r\n        [DataMember(EmitDefaultValue = false)]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }
        }

        [Fact]
        public async Task BadOrderAsync()
        {
            // multiple declarations
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadOrders
    {
        [SerializerMember(
            FormatterType=typeof(BadOrders),
            FormatterMethodName = nameof(ForInt),

            ShouldSerializeType = typeof(BadOrders),
            ShouldSerializeMethodName = nameof(ShouldSerializeBar),

            Order = 2
        )]
        [DataMember(Order = 2)]
        public int Bar { get; set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(BadOrders row, in WriteContext ctx)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.OrderSpecifiedMultipleTimes, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType=typeof(BadOrders),\r\n            FormatterMethodName = nameof(ForInt),\r\n\r\n            ShouldSerializeType = typeof(BadOrders),\r\n            ShouldSerializeMethodName = nameof(ShouldSerializeBar),\r\n\r\n            Order = 2\r\n        )]\r\n        [DataMember(Order = 2)]\r\n        public int Bar { get; set; }\r\n", GetFlaggedSource(d));
                    }
                );
            }
        }

        [Fact]
        public async Task MissingMethodNameAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class MissingMethodNames
    {
        [SerializerMember(
            FormatterType = typeof(MissingMethodNames),
            FormatterMethodName = nameof(ForInt),
        )]
        public int Bar() => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(BadOrders row, in WriteContext ctx)
        => false;
    }
}", gen);

            Assert.Collection(
                diags,
                d =>
                {
                    AssertDiagnostic(Diagnostics.SerializableMemberMustHaveNameSetForMethod, d);
                    Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(MissingMethodNames),\r\n            FormatterMethodName = nameof(ForInt),\r\n        )]\r\n        public int Bar() => 1;\r\n", GetFlaggedSource(d));
                }
            );
        }

        [Fact]
        public async Task MethodCannotReturnVoidAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class MethodCannotReturnVoids
    {
        [SerializerMember(
            FormatterType = typeof(MethodCannotReturnVoids),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public void Bar() { }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(MethodCannotReturnVoids row, in WriteContext ctx)
        => false;
    }
}", gen);

            Assert.Collection(
                diags,
                d =>
                {
                    AssertDiagnostic(Diagnostics.MethodMustReturnNonVoid, d);
                    Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(MethodCannotReturnVoids),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public void Bar() { }\r\n", GetFlaggedSource(d));
                }
            );
        }

        [Fact]
        public async Task PropertyMustHaveGetterAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class PropertyMustHaveGetters
    {
        [SerializerMember(
            FormatterType = typeof(PropertyMustHaveGetters),
            FormatterMethodName = nameof(ForInt)
        )]
        public int Bar { set; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(PropertyMustHaveGetters row, in WriteContext ctx)
        => false;
    }
}", gen);

            Assert.Collection(
                diags,
                d =>
                {
                    AssertDiagnostic(Diagnostics.NoGetterOnSerializableProperty, d);
                    Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(PropertyMustHaveGetters),\r\n            FormatterMethodName = nameof(ForInt)\r\n        )]\r\n        public int Bar { set; }\r\n", GetFlaggedSource(d));
                }
            );
        }

        [Fact]
        public async Task PropertyCannotHaveParametersAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class PropertyCannotHaveParameters
    {
        [SerializerMember(
            FormatterType = typeof(PropertyCannotHaveParameters),
            FormatterMethodName = nameof(ForInt)
        )]
        public int this[int ix] { get => 2; }

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;

        public static bool ShouldSerializeBar(PropertyCannotHaveParameters row, in WriteContext ctx)
        => false;
    }
}", gen);

            Assert.Collection(
                diags,
                d =>
                {
                    AssertDiagnostic(Diagnostics.SerializablePropertyCannotHaveParameters, d);
                    Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(PropertyCannotHaveParameters),\r\n            FormatterMethodName = nameof(ForInt)\r\n        )]\r\n        public int this[int ix] { get => 2; }\r\n", GetFlaggedSource(d));
                }
            );
        }

        [Fact]
        public async Task DataMemberOrderUnspecifiedAsync()
        {
            // implicit
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class DataMemberOrderUnspecifieds
    {
        [SerializerMember(
            FormatterType = typeof(DataMemberOrderUnspecifieds),
            FormatterMethodName = nameof(Formatter)
        )]
        [DataMember]
        public int Bar;

        internal static bool Formatter(int val, in WriteContext cxt, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Empty(diags);

                var member = gen.Members.Single().Value.Single();

                Assert.Null(member.Order);
            }

            // explicitly -1
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class DataMemberOrderUnspecifieds
    {
        [SerializerMember(
            FormatterType = typeof(DataMemberOrderUnspecifieds),
            FormatterMethodName = nameof(Formatter)
        )]
        [DataMember(Order = -1)]
        public int Bar;

        internal static bool Formatter(int val, in WriteContext cxt, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Empty(diags);

                var member = gen.Members.Single().Value.Single();

                Assert.Null(member.Order);
            }
        }

        [Fact]
        public async Task BadGetterMethodParametersAsync()
        {
            // static, too many parameters
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(int a, int b, int c) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_TooMany, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(int a, int b, int c) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, one parameter, WriteContext not in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(WriteContext ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(WriteContext ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, one paramter, wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(string row) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(string row) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, one paramter, right type but iun
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(in BadGetterMethodParameters row) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(in BadGetterMethodParameters row) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two paramters, wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(string row, in WriteContext ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(string row, in WriteContext ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two paramters, right type but in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(in BadGetterMethodParameters row, in WriteContext ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(in BadGetterMethodParameters row, in WriteContext ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two paramters, write context not in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(BadGetterMethodParameters row, WriteContext ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(BadGetterMethodParameters row, WriteContext ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // static, two paramters, not WriteContext
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public static int Bar(BadGetterMethodParameters row, in string ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_StaticTwo, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public static int Bar(BadGetterMethodParameters row, in string ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, too many parameters
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public int Bar(int a, int b, int c) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_TooMany, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public int Bar(int a, int b, int c) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, one parameter, wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public int Bar(int a) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_InstanceOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public int Bar(int a) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, one parameter, WriteContext not in
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public int Bar(WriteContext ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_InstanceOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public int Bar(WriteContext ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }

            // instance, one parameter, in wrong type
            {
                var gen = new SerializerGenerator();
                var (_, diags) = await RunSourceGeneratorAsync(
    @"
using System;
using System.Buffers;
using Cesil;
using System.Runtime.Serialization;

namespace Foo 
{   
    [GenerateSerializer]
    class BadGetterMethodParameters
    {
        [SerializerMember(
            FormatterType = typeof(BadGetterMethodParameters),
            FormatterMethodName = nameof(ForInt),
            Name = ""Bar""
        )]
        public int Bar(in string ctx) => 1;

        public static bool ForInt(int val, in WriteContext ctx, IBufferWriter<char> buffer)
        => false;
    }
}", gen);

                Assert.Collection(
                    diags,
                    d =>
                    {
                        AssertDiagnostic(Diagnostics.BadGetterParameters_InstanceOne, d);
                        Assert.Equal("        [SerializerMember(\r\n            FormatterType = typeof(BadGetterMethodParameters),\r\n            FormatterMethodName = nameof(ForInt),\r\n            Name = \"Bar\"\r\n        )]\r\n        public int Bar(in string ctx) => 1;\r\n", GetFlaggedSource(d));
                    }
                );
            }
        }

        [Fact]
        public async Task DefaultFormattersAsync()
        {
            var gen = new SerializerGenerator();
            var (_, diags) = await RunSourceGeneratorAsync(
@"
using System;
using System.Buffers;
using Cesil;

namespace Foo 
{   
    [GenerateSerializer]
    class WriteMe
    {
        [SerializerMember]
        public int Bar { get; set; }
        [SerializerMember]
        public string Fizz;
        [SerializerMember(Name=""Hello"")]
        public DateTime SomeMtd() => DateTime.Now;
    }
}", gen);

            Assert.Empty(diags);

            Assert.Collection(
                gen.NeededDefaultFormatters,
                forBar =>
                {
                    Assert.True(forBar.IsDefault);
                    Assert.Equal("System.Int32", forBar.ForDefaultType);
                },
                forFizz =>
                {
                    Assert.True(forFizz.IsDefault);
                    Assert.Equal("System.String?", forFizz.ForDefaultType);
                },
                forHello =>
                {
                    Assert.True(forHello.IsDefault);
                    Assert.Equal("System.DateTime", forHello.ForDefaultType);
                }
            );
        }

        private static void AssertDiagnostic(Func<Location, Diagnostic> shouldBe, Diagnostic actuallyIs)
        {
            var id = shouldBe(null).Id;
            Assert.Equal(id, actuallyIs.Id);
        }

        private void AssertDiagnostic(Func<Location, IMethodSymbol, Diagnostic> shouldBe, Diagnostic actuallyIs)
        {
            var id = shouldBe(null, Method).Id;
            Assert.Equal(id, actuallyIs.Id);
        }

        private void AssertDiagnostic(Func<Location, string, string, Diagnostic> shouldBe, Diagnostic actuallyIs)
        {
            var id = shouldBe(null, "a", "b").Id;
            Assert.Equal(id, actuallyIs.Id);
        }

        private void AssertDiagnostic(Func<Location, IMethodSymbol, ITypeSymbol, Diagnostic> shouldBe, Diagnostic actuallyIs)
        {
            var id = shouldBe(null, Method, Type).Id;
            Assert.Equal(id, actuallyIs.Id);
        }
    }
}

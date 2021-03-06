﻿using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Cesil.Analyzers
{
    /// <summary>
    /// Looks for direct use of IsComplete, IsCanceled, IsFaulted, or IsCompletedSuccessfully
    ///   on Task or ValueTask
    ///   
    /// If any of those are used, flag them with a suggestion to use the IsCompletedSuccessfully()
    ///   extension method instead
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IsCompletedSuccessfullyAnalyzer : AnalyzerBase<IsCompletedSuccessfullyAnalyzer.State>
    {
        public sealed class State
        {
            public ImmutableArray<SourceSpan> AsyncTestHelperRoots { get; }
            public ImmutableHashSet<IPropertySymbol> ForbiddenMembers { get; }

            internal State(ImmutableArray<SourceSpan> asyncTestHelperRoots, ImmutableHashSet<IPropertySymbol> forbiddenMembers)
            {
                AsyncTestHelperRoots = asyncTestHelperRoots;
                ForbiddenMembers = forbiddenMembers;
            }
        }

        private static ImmutableHashSet<string> TaskProperties { get; } = ImmutableHashSet.Create(nameof(Task.IsCompleted), nameof(Task.IsCanceled), nameof(Task.IsFaulted), nameof(ValueTask.IsCompletedSuccessfully));

        public IsCompletedSuccessfullyAnalyzer() : base(false, Diagnostics.IsCompletedSuccessfully, SyntaxKind.SimpleMemberAccessExpression) { }

        protected override State OnCompilationStart(Compilation compilation)
        {
            // single class is allowed to use the forbidden members without explanation
            var asyncTestHelper = compilation.GetTypeByMetadataNameNonNull("Cesil.AsyncTestHelper");

            var asyncTestHelperRoots = asyncTestHelper.GetSourceSpans();

            // get the stuff that we want to flag ONCE per compilation
            var task = compilation.GetTypeByMetadataNameNonNull("System.Threading.Tasks.Task");
            var taskT = compilation.GetTypeByMetadataNameNonNull("System.Threading.Tasks.Task`1");
            var valueTask = compilation.GetTypeByMetadataNameNonNull("System.Threading.Tasks.ValueTask");
            var valueTaskT = compilation.GetTypeByMetadataNameNonNull("System.Threading.Tasks.ValueTask`1");

            var ret = ImmutableHashSet.CreateBuilder<IPropertySymbol>();

            foreach (var sym in new[] { task, taskT, valueTask, valueTaskT })
            {
                foreach (var name in TaskProperties)
                {
                    var mems = sym.GetMembers(name);
                    foreach (var mem in mems)
                    {
                        var prop = mem.Expect<ISymbol, IPropertySymbol>();
                        ret.Add(prop);
                    }
                }
            }

            return new State(asyncTestHelperRoots, ret.ToImmutable());
        }

        protected override void OnSyntaxNode(SyntaxNodeAnalysisContext context, State state)
        {
            var expr = context.Node.Expect<SyntaxNode, MemberAccessExpressionSyntax>();

            var name = expr.Name.Identifier.ValueText;
            if (!TaskProperties.Contains(name))
            {
                // early check, we _know_ this can't be something we care about
                return;
            }

            // now actually do the expensive parts, checking that the name isn't just a coincidence
            var model = context.SemanticModel;

            var mark = false;

            var bound = model.GetSymbolInfo(expr);
            if (bound.Symbol is IPropertySymbol prop)
            {
                var originalProp = prop.OriginalDefinition;
                mark = state.ForbiddenMembers.Contains(originalProp);
            }

            if (mark)
            {
                var inAsyncTestHelper = state.AsyncTestHelperRoots.ContainsNode(expr);
                if (inAsyncTestHelper)
                {
                    // no need to check uses in AsyncTestHelper, since that's what we suggest you use instead
                    return;
                }

                expr.ReportDiagnostic(Diagnostics.IsCompletedSuccessfully, context);
            }
        }
    }
}

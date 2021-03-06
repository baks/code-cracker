using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Style
{
    public sealed class UseEmptyStringCodeFixAllProvider : FixAllProvider
    {
        private static readonly SyntaxAnnotation useEmptyStringAnnotation = new SyntaxAnnotation(nameof(UseEmptyStringCodeFixAllProvider));
        private UseEmptyStringCodeFixAllProvider() { }
        public static UseEmptyStringCodeFixAllProvider Instance = new UseEmptyStringCodeFixAllProvider();

        public override Task<CodeAction> GetFixAsync(FixAllContext fixAllContext)
        {
            switch (fixAllContext.Scope)
            {
                case FixAllScope.Document:
                    {
                        return Task.FromResult(CodeAction.Create(UseEmptyStringCodeFixProvider.MessageFormat,
                            async ct =>
                            {
                                var newFixAllContext = fixAllContext.WithCancellationToken(ct);
                                var diagnostics = await newFixAllContext.GetDocumentDiagnosticsAsync(newFixAllContext.Document).ConfigureAwait(false);
                                var root = await GetFixedDocumentAsync(newFixAllContext.Document, diagnostics, ct).ConfigureAwait(false);
                                return newFixAllContext.Document.WithSyntaxRoot(root);
                            }));

                    }
                case FixAllScope.Project:
                    return Task.FromResult(CodeAction.Create(UseEmptyStringCodeFixProvider.MessageFormat,
                        ct =>
                        {
                            var newFixAllContext = fixAllContext.WithCancellationToken(ct);
                            return GetFixedProjectAsync(newFixAllContext, newFixAllContext.WithCancellationToken(ct).Project);
                        }));
                case FixAllScope.Solution:
                    return Task.FromResult(CodeAction.Create(UseEmptyStringCodeFixProvider.MessageFormat,
                        ct => GetFixedSolutionAsync(fixAllContext.WithCancellationToken(ct))));
            }
            return null;
        }

        private async static Task<Solution> GetFixedSolutionAsync(FixAllContext fixAllContext)
        {
            var newSolution = fixAllContext.Solution;
            foreach (var projectId in newSolution.ProjectIds)
                newSolution = await GetFixedProjectAsync(fixAllContext, newSolution.GetProject(projectId)).ConfigureAwait(false);
            return newSolution;
        }

        private async static Task<Solution> GetFixedProjectAsync(FixAllContext fixAllContext, Project project)
        {
            var solution = project.Solution;
            foreach (var document in project.Documents)
            {
                var diagnostics = await fixAllContext.GetDocumentDiagnosticsAsync(document).ConfigureAwait(false);
                var newRoot = await GetFixedDocumentAsync(document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false);
                solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
            }
            return solution;
        }

        private async static Task<SyntaxNode> GetFixedDocumentAsync(Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var nodes = diagnostics.Select(d => root.FindNode(d.Location.SourceSpan)).Where(n => !n.IsMissing).Select(n => n.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().First());
            var newRoot = root.ReplaceNodes(nodes, (original, rewritten) => original.WithAdditionalAnnotations(useEmptyStringAnnotation));
            while (true)
            {
                var annotatedNodes = newRoot.GetAnnotatedNodes(useEmptyStringAnnotation);
                var node = annotatedNodes.FirstOrDefault();
                if (node == null) break;
                var literal = (MemberAccessExpressionSyntax)node;
                newRoot = newRoot.ReplaceNode(literal, SyntaxFactory.ParseExpression("\"\"").WithLeadingTrivia(literal.GetLeadingTrivia()).WithTrailingTrivia(literal.GetTrailingTrivia()));
            }
            return newRoot;
        }
    }
}
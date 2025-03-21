using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using ZLinq;

namespace boilersExtensions
{
    public class NamespaceRenamer
    {
        public static async Task RenameNamespaceAsync(string solutionPath, string oldNamespace, string newNamespace)
        {
            using (var workspace = MSBuildWorkspace.Create())
            {
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                foreach (var projectId in solution.ProjectIds)
                {
                    var project = solution.GetProject(projectId);
                    foreach (var documentId in project.DocumentIds)
                    {
                        var document = project.GetDocument(documentId);
                        var syntaxRoot = await document.GetSyntaxRootAsync();

                        var namespaceDeclarations = syntaxRoot.DescendantNodes().AsValueEnumerable()
                            .OfType<NamespaceDeclarationSyntax>()
                            .Where(nd => nd.Name.ToString() == oldNamespace)
                            .ToList();

                        foreach (var namespaceDeclaration in namespaceDeclarations)
                        {
                            var newNamespaceDeclaration =
                                namespaceDeclaration.WithName(SyntaxFactory.ParseName(newNamespace));
                            syntaxRoot = syntaxRoot.ReplaceNode(namespaceDeclaration, newNamespaceDeclaration);
                        }

                        var formattedRoot = Formatter.Format(syntaxRoot, workspace);
                        var newDocument = document.WithSyntaxRoot(formattedRoot);
                        project = newDocument.Project;
                    }

                    solution = project.Solution;
                }

                workspace.TryApplyChanges(solution);
            }
        }
    }
}
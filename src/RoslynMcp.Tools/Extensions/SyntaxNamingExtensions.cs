using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tools.Extensions;

public static class SyntaxNamingExtensions
{
	public static string GetNamespaceName(this SyntaxNode node)
	{
		var segments = new Stack<string>();

		for (SyntaxNode? current = node; current is not null; current = current.Parent)
		{
			if (current is BaseNamespaceDeclarationSyntax ns)
				segments.Push(ns.Name.ToString());
		}

		return segments.Count == 0 ? string.Empty : string.Join(".", segments);
	}

	public static string GetContainingTypeChain(this SyntaxNode node)
	{
		var segments = new Stack<string>();

		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			if (current is TypeDeclarationSyntax t)
				segments.Push(BuildTypeIdentity(t.Identifier.ValueText, t.TypeParameterList?.Parameters.Count ?? 0));
		}

		return segments.Count == 0 ? string.Empty : string.Join(".", segments);
	}

	public static string BuildTypeIdentity(string name, int genericArity)
		=> genericArity > 0 ? $"{name}`{genericArity}" : name;
}


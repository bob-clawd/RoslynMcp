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

	public static string GetContainingTypeLikeChain(this SyntaxNode node)
	{
		var segments = new Stack<string>();

		for (var current = node.Parent; current is not null; current = current.Parent)
		{
			switch (current)
			{
				case TypeDeclarationSyntax t:
					segments.Push(BuildTypeIdentity(t.Identifier.ValueText, t.TypeParameterList?.Parameters.Count ?? 0));
					break;
				case EnumDeclarationSyntax e:
					segments.Push(BuildTypeIdentity(e.Identifier.ValueText, genericArity: 0));
					break;
				case DelegateDeclarationSyntax d:
					segments.Push(BuildTypeIdentity(d.Identifier.ValueText, d.TypeParameterList?.Parameters.Count ?? 0));
					break;
			}
		}

		return segments.Count == 0 ? string.Empty : string.Join(".", segments);
	}

	/// <summary>
	/// Builds the metadata-like type identity used by the syntax search tools.
	/// Examples: <c>Foo</c> => <c>Foo</c>, <c>Foo&lt;T&gt;</c> => <c>Foo`1</c>, <c>Foo&lt;T1, T2&gt;</c> => <c>Foo`2</c>.
	/// </summary>
	public static string BuildTypeIdentity(string name, int genericArity)
		=> genericArity > 0 ? $"{name}`{genericArity}" : name;

	/// <summary>
	/// Combines a containing type chain with the local metadata-like identity.
	/// Example: <c>Outer`1</c> + <c>Inner`2</c> => <c>Outer`1.Inner`2</c>.
	/// </summary>
	public static string BuildQualifiedTypeIdentity(string container, string name, int genericArity)
	{
		var local = BuildTypeIdentity(name, genericArity);
		return string.IsNullOrWhiteSpace(container) ? local : $"{container}.{local}";
	}
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcp.Tools.Extensions;

/// <summary>
/// Stateless helpers for building stable, metadata-like names from Roslyn syntax.
/// Used by syntax-only search tools (e.g. <c>search_member</c>, <c>search_type</c>) to keep output consistent.
/// </summary>
public static class SyntaxNamingExtensions
{
	/// <summary>
	/// Returns the dotted namespace name for the node (e.g. <c>Company.Product.Module</c>),
	/// or an empty string for the global namespace.
	/// </summary>
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

	/// <summary>
	/// Returns the containing type chain for nested declarations (types only).
	/// Uses metadata-like generic arity encoding (<c>Foo&lt;T&gt;</c> => <c>Foo`1</c>) to keep names short and stable.
	/// </summary>
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

	/// <summary>
	/// Returns the containing chain for nested declarations including types, enums and delegates.
	/// Uses metadata-like generic arity encoding (<c>Foo&lt;T&gt;</c> => <c>Foo`1</c>).
	/// </summary>
	public static string GetContainingTypeLikeChain(this SyntaxNode node)
	{
		// Build the chain of containing type identities for nested declarations.
		// Includes types, enums, and delegates.
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
	/// Builds a metadata-like identity for a type-like name.
	/// Examples:
	/// <list type="bullet">
	/// <item><description><c>Foo</c> => <c>Foo</c></description></item>
	/// <item><description><c>Foo&lt;T&gt;</c> => <c>Foo`1</c></description></item>
	/// <item><description><c>Foo&lt;T1,T2&gt;</c> => <c>Foo`2</c></description></item>
	/// </list>
	/// </summary>
	public static string BuildTypeIdentity(string name, int genericArity)
		=> genericArity > 0 ? $"{name}`{genericArity}" : name;

	/// <summary>
	/// Builds a qualified type identity from a container chain and local type identity.
	/// </summary>
	public static string BuildQualifiedTypeIdentity(string container, string name, int genericArity)
	{
		var local = BuildTypeIdentity(name, genericArity);
		return string.IsNullOrWhiteSpace(container) ? local : $"{container}.{local}";
	}
}

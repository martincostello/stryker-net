using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Stryker.Abstractions.Options;
using Stryker.Abstractions;

namespace Stryker.Core.Mutators;

/// <summary>
/// Mutators can implement this class to check the type of the node and cast the node to the expected type.
/// Implementing this class is not obligatory for mutators.
/// </summary>
/// <typeparam name="T">The type of SyntaxNode to cast to</typeparam>
public abstract class MutatorBase<T> : IMutator where T : SyntaxNode
{
    /// <summary>
    /// Apply the given mutations to a single SyntaxNode
    /// </summary>
    /// <param name="node">The node to mutate</param>
    /// <returns>One or more mutations</returns>
    public abstract IEnumerable<Mutation> ApplyMutations(T node, SemanticModel semanticModel);

    public abstract MutationLevel MutationLevel { get; }

    public IEnumerable<Mutation> Mutate(SyntaxNode node, SemanticModel semanticModel, IStrykerOptions options)
    {
        if (MutationLevel <= options.MutationLevel && node is T tNode)
        {
            // the node was of the expected type, so invoke the mutation method
            return ApplyMutations(tNode, semanticModel);
        }

        return Enumerable.Empty<Mutation>();
    }
}

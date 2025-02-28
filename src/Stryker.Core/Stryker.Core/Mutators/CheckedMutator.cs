using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Abstractions;
using Stryker.Core.Helpers;
using System.Collections.Generic;

namespace Stryker.Core.Mutators;

public class CheckedMutator : MutatorBase<CheckedExpressionSyntax>
{
    public override MutationLevel MutationLevel => MutationLevel.Standard;

    public override IEnumerable<Mutation> ApplyMutations(CheckedExpressionSyntax node, SemanticModel semanticModel)
    {
        if (node.Kind() == SyntaxKind.CheckedExpression)
        {
            yield return new Mutation()
            {
                OriginalNode = node,
                ReplacementNode = node.Expression.WithCleanTrivia(),
                DisplayName = "Remove checked expression",
                Type = Mutator.Checked
            };
        }
    }
}

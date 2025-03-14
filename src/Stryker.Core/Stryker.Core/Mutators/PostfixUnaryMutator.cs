using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stryker.Abstractions;
using Stryker.Core.Helpers;

namespace Stryker.Core.Mutators;

public class PostfixUnaryMutator : MutatorBase<PostfixUnaryExpressionSyntax>
{
    public override MutationLevel MutationLevel => MutationLevel.Standard;

    public override IEnumerable<Mutation> ApplyMutations(PostfixUnaryExpressionSyntax node, SemanticModel semanticModel)
    {
        var unaryKind = node.Kind();
        SyntaxKind newKind;
        if (unaryKind == SyntaxKind.PostIncrementExpression)
        {
            newKind = SyntaxKind.PostDecrementExpression;
        }
        else if (unaryKind == SyntaxKind.PostDecrementExpression)
        {
            newKind = SyntaxKind.PostIncrementExpression;
        }
        else
        {
            yield break;
        }

        yield return new Mutation
        {
            OriginalNode = node,
            ReplacementNode = SyntaxFactory.PostfixUnaryExpression(newKind, node.Operand.WithCleanTrivia()),
            DisplayName = $"{unaryKind} to {newKind} mutation",
            Type = Mutator.Update
        };
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Analyzers.Constants;
using NUnit.Analyzers.Extensions;

namespace NUnit.Analyzers.TestCaseSourceUsage
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TestCaseSourceUsesStringAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor missingSourceDescriptor = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceIsMissing,
            title: "TestCaseSource argument does not specify an existing member.",
            messageFormat: "TestCaseSource argument '{0}' does not specify an existing member.",
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: "TestCaseSource argument does not specify an existing member. This will lead to an error at run-time.");

        private static readonly DiagnosticDescriptor considerNameOfDescriptor = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceStringUsage,
            title: TestCaseSourceUsageConstants.ConsiderNameOfInsteadOfStringConstantAnalyzerTitle,
            messageFormat: TestCaseSourceUsageConstants.ConsiderNameOfInsteadOfStringConstantMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Warning,
            description: TestCaseSourceUsageConstants.ConsiderNameOfInsteadOfStringConstantDescription);

        private static readonly DiagnosticDescriptor sourceTypeNotIEnumerableDescriptor = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceSourceTypeNotIEnumerable,
            title: TestCaseSourceUsageConstants.SourceTypeNotIEnumerableTitle,
            messageFormat: TestCaseSourceUsageConstants.SourceTypeNotIEnumerableMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.SourceTypeNotIEnumerableDescription);

        private static readonly DiagnosticDescriptor sourceTypeNoDefaultConstructorDescriptor = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceSourceTypeNoDefaultConstructor,
            title: TestCaseSourceUsageConstants.SourceTypeNoDefaultConstructorTitle,
            messageFormat: TestCaseSourceUsageConstants.SourceTypeNoDefaultConstructorMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.SourceTypeNoDefaultConstructorDescription);

        private static readonly DiagnosticDescriptor sourceNotStaticDescriptor = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceSourceIsNotStatic,
            title: TestCaseSourceUsageConstants.SourceIsNotStaticTitle,
            messageFormat: TestCaseSourceUsageConstants.SourceIsNotStaticMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.SourceIsNotStaticDescription);

        private static readonly DiagnosticDescriptor mismatchInNumberOfParameters = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceMismatchInNumberOfParameters,
            title: TestCaseSourceUsageConstants.MismatchInNumberOfParametersTitle,
            messageFormat: TestCaseSourceUsageConstants.MismatchInNumberOfParametersMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.MismatchInNumberOfParametersDescription);

        private static readonly DiagnosticDescriptor sourceDoesNotReturnIEnumerable = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceDoesNotReturnIEnumerable,
            title: TestCaseSourceUsageConstants.SourceDoesNotReturnIEnumerableTitle,
            messageFormat: TestCaseSourceUsageConstants.SourceDoesNotReturnIEnumerableMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.SourceDoesNotReturnIEnumerableDescription);

        private static readonly DiagnosticDescriptor parametersSuppliedToFieldOrProperty = DiagnosticDescriptorCreator.Create(
            id: AnalyzerIdentifiers.TestCaseSourceSuppliesParametersToFieldOrProperty,
            title: TestCaseSourceUsageConstants.TestCaseSourceSuppliesParametersTitle,
            messageFormat: TestCaseSourceUsageConstants.TestCaseSourceSuppliesParametersMessage,
            category: Categories.Structure,
            defaultSeverity: DiagnosticSeverity.Error,
            description: TestCaseSourceUsageConstants.TestCaseSourceSuppliesParametersDescription);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            considerNameOfDescriptor,
            missingSourceDescriptor,
            sourceTypeNotIEnumerableDescriptor,
            sourceTypeNoDefaultConstructorDescriptor,
            sourceNotStaticDescriptor,
            mismatchInNumberOfParameters,
            sourceDoesNotReturnIEnumerable,
            parametersSuppliedToFieldOrProperty);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(x => AnalyzeAttribute(x), SyntaxKind.Attribute);
        }

        private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
        {
            var testCaseSourceType = context.SemanticModel.Compilation.GetTypeByMetadataName(NunitFrameworkConstants.FullNameOfTypeTestCaseSourceAttribute);
            if (testCaseSourceType == null)
            {
                return;
            }

            var attributeNode = (AttributeSyntax)context.Node;
            var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeNode).Symbol;

            if (testCaseSourceType.ContainingAssembly.Identity == attributeSymbol?.ContainingAssembly.Identity &&
                NunitFrameworkConstants.NameOfTestCaseSourceAttribute == attributeSymbol?.ContainingType.Name)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var attributeInfo = ExtractInfoFromAttribute(context, attributeNode);

                if (attributeInfo == null)
                {
                    return;
                }

                var stringConstant = attributeInfo.SourceName;

                if (stringConstant is null && attributeNode.ArgumentList.Arguments.Count == 1)
                {
                    // The Type argument in this form represents the class that provides test cases.
                    // It must have a default constructor and implement IEnumerable.
                    var sourceType = attributeInfo.SourceType;
                    bool typeImplementsIEnumerable = sourceType.IsIEnumerable(out _);
                    bool typeHasDefaultConstructor = sourceType.Constructors.Any(c => c.Parameters.IsEmpty);
                    if (!typeImplementsIEnumerable)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            sourceTypeNotIEnumerableDescriptor,
                            attributeNode.ArgumentList.Arguments[0].GetLocation(),
                            sourceType.Name));
                    }
                    else if (!typeHasDefaultConstructor)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            sourceTypeNoDefaultConstructorDescriptor,
                            attributeNode.ArgumentList.Arguments[0].GetLocation(),
                            sourceType.Name));
                    }

                    return;
                }

                var syntaxNode = attributeInfo.SyntaxNode;

                if (syntaxNode == null || stringConstant == null)
                {
                    return;
                }

                var symbol = GetMember(context, attributeInfo);
                if (symbol is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                       missingSourceDescriptor,
                       syntaxNode.GetLocation(),
                       stringConstant));
                }
                else
                {
                    var sourceIsAccessible = context.SemanticModel.IsAccessible(
                        syntaxNode.GetLocation().SourceSpan.Start,
                        symbol);

                    if (attributeInfo.IsStringLiteral && sourceIsAccessible)
                    {
                        var nameOfClassTarget = attributeInfo.SourceType.ToMinimalDisplayString(
                            context.SemanticModel,
                            syntaxNode.GetLocation().SourceSpan.Start);


                        var nameOfTarget = attributeInfo.SourceType == context.ContainingSymbol.ContainingType
                            ? stringConstant
                            : $"{nameOfClassTarget}.{stringConstant}";

                        var properties = new Dictionary<string, string>
                        {
                            { TestCaseSourceUsageConstants.PropertyKeyNameOfTarget, nameOfTarget }
                        };

                        context.ReportDiagnostic(Diagnostic.Create(
                            considerNameOfDescriptor,
                            syntaxNode.GetLocation(),
                            properties.ToImmutableDictionary(),
                            nameOfTarget,
                            stringConstant));
                    }

                    if (!symbol.IsStatic)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            sourceNotStaticDescriptor,
                            syntaxNode.GetLocation(),
                            stringConstant));
                    }

                    switch (symbol)
                    {
                        case IPropertySymbol property:
                            ReportIfSymbolNotIEnumerable(context, syntaxNode, property.Type);
                            ReportIfParametersSupplied(context, syntaxNode, attributeInfo.NumberOfMethodParameters, "properties");
                            break;
                        case IFieldSymbol field:
                            ReportIfSymbolNotIEnumerable(context, syntaxNode, field.Type);
                            ReportIfParametersSupplied(context, syntaxNode, attributeInfo.NumberOfMethodParameters, "fields");
                            break;
                        case IMethodSymbol method:
                            ReportIfSymbolNotIEnumerable(context, syntaxNode, method.ReturnType);

                            var methodParametersFromAttribute = attributeInfo.NumberOfMethodParameters ?? 0;
                            if (method.Parameters.Length != methodParametersFromAttribute)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    mismatchInNumberOfParameters,
                                    syntaxNode.GetLocation(),
                                    methodParametersFromAttribute,
                                    method.Parameters.Length));
                            }
                            break;
                    }
                }
            }
        }

        private static void ReportIfSymbolNotIEnumerable(
            SyntaxNodeAnalysisContext context,
            SyntaxNode syntaxNode,
            ITypeSymbol typeSymbol)
        {
            if (!typeSymbol.IsIEnumerable(out var _))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    sourceDoesNotReturnIEnumerable,
                    syntaxNode.GetLocation(),
                    typeSymbol));
            }
        }

        private static void ReportIfParametersSupplied(
            SyntaxNodeAnalysisContext context,
            SyntaxNode syntaxNode,
            int? numberOfMethodParameters,
            string kind)
        {
            if (numberOfMethodParameters > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    parametersSuppliedToFieldOrProperty,
                    syntaxNode.GetLocation(),
                    numberOfMethodParameters,
                    kind));
            }
        }

        private static SourceAttributeInformation? ExtractInfoFromAttribute(
            SyntaxNodeAnalysisContext context,
            AttributeSyntax attributeSyntax)
        {
            var (positionalArguments, _) = attributeSyntax.GetArguments();

            if (positionalArguments.Length < 1)
            {
                return null;
            }

            var firstArgumentExpression = positionalArguments[0]?.Expression;
            if (firstArgumentExpression == null)
            {
                return null;
            }

            // TestCaseSourceAttribute has the following constructors:
            // * TestCaseSourceAttribute(Type sourceType)
            // * TestCaseSourceAttribute(Type sourceType, string sourceName)
            // * TestCaseSourceAttribute(Type sourceType, string sourceName, object?[]? methodParams)
            // * TestCaseSourceAttribute(string sourceName)
            // * TestCaseSourceAttribute(string sourceName, object?[]? methodParams)
            if (firstArgumentExpression is TypeOfExpressionSyntax typeofSyntax)
            {
                var sourceType = context.SemanticModel.GetSymbolInfo(typeofSyntax.Type).Symbol as INamedTypeSymbol;
                return ExtractElementsInAttribute(context, sourceType, positionalArguments, 1);
            }
            else
            {
                var sourceType = context.ContainingSymbol.ContainingType;
                return ExtractElementsInAttribute(context, sourceType, positionalArguments, 0);
            }
        }

        private static SourceAttributeInformation? ExtractElementsInAttribute(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? sourceType,
            ImmutableArray<AttributeArgumentSyntax> positionalArguments,
            int sourceNameIndex)
        {
            if (sourceType == null)
            {
                return null;
            }

            SyntaxNode? syntaxNode = null;
            string? sourceName = null;
            bool isStringLiteral = false;
            if (positionalArguments.Length > sourceNameIndex)
            {
                var syntaxNameAndType = GetSyntaxStringConstantAndType(context, positionalArguments, sourceNameIndex);

                if (syntaxNameAndType == null)
                {
                    return null;
                }

                (syntaxNode, sourceName, isStringLiteral) = syntaxNameAndType.Value;
            }

            int? numMethodParams = null;
            if (positionalArguments.Length > sourceNameIndex + 1)
            {
                numMethodParams = GetNumberOfParametersToMethod(positionalArguments[sourceNameIndex + 1]);
            }

            return new SourceAttributeInformation(sourceType, sourceName, syntaxNode, isStringLiteral, numMethodParams);
        }

        private static (SyntaxNode syntaxNode, string sourceName, bool isLiteral)? GetSyntaxStringConstantAndType(
            SyntaxNodeAnalysisContext context,
            ImmutableArray<AttributeArgumentSyntax> arguments,
            int index)
        {
            if (index >= arguments.Length)
            {
                return null;
            }

            var argumentSyntax = arguments[index];

            if (argumentSyntax == null)
            {
                return null;
            }

            Optional<object> possibleConstant = context.SemanticModel.GetConstantValue(argumentSyntax.Expression);

            if (possibleConstant.HasValue && possibleConstant.Value is string stringConstant)
            {
                SyntaxNode syntaxNode = argumentSyntax.Expression;
                bool isStringLiteral = syntaxNode is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression);

                return (syntaxNode, stringConstant, isStringLiteral);
            }

            return null;
        }

        private static int? GetNumberOfParametersToMethod(AttributeArgumentSyntax attributeArgumentSyntax)
        {
            var lastExpression = attributeArgumentSyntax?.Expression as ArrayCreationExpressionSyntax;
            return lastExpression?.Initializer.Expressions.Count;
        }

        private static ISymbol? GetMember(SyntaxNodeAnalysisContext context, SourceAttributeInformation attributeInformation)
        {
            if (attributeInformation.SyntaxNode == null || !SyntaxFacts.IsValidIdentifier(attributeInformation.SourceName))
            {
                return null;
            }

            foreach (var syntaxReference in attributeInformation.SourceType.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is ClassDeclarationSyntax syntax)
                {
                    var classIdentifier = syntax.Identifier;

                    var symbols = context.SemanticModel.LookupSymbols(
                        classIdentifier.Span.Start,
                        container: attributeInformation.SourceType,
                        name: attributeInformation.SourceName);

                    foreach (var symbol in symbols)
                    {
                        switch (symbol.Kind)
                        {
                            case SymbolKind.Field:
                            case SymbolKind.Property:
                            case SymbolKind.Method:
                                return symbol;
                        }
                    }
                }
            }

            return null;
        }
    }
}

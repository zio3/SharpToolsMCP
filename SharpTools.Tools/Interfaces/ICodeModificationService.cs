namespace SharpTools.Tools.Interfaces;
public interface ICodeModificationService {
    Task<Solution> AddMemberAsync(DocumentId documentId, INamedTypeSymbol targetTypeSymbol, MemberDeclarationSyntax newMember, int lineNumberHint = -1, CancellationToken cancellationToken = default);
    Task<Solution> AddStatementAsync(DocumentId documentId, MethodDeclarationSyntax targetMethod, StatementSyntax newStatement, CancellationToken cancellationToken, bool addToBeginning = false);
    Task<Solution> ReplaceNodeAsync(DocumentId documentId, SyntaxNode oldNode, SyntaxNode newNode, CancellationToken cancellationToken);
    Task<Solution> RenameSymbolAsync(ISymbol symbol, string newName, CancellationToken cancellationToken);
    Task<Solution> ReplaceAllReferencesAsync(ISymbol symbol, string replacementText, CancellationToken cancellationToken, Func<SyntaxNode, bool>? predicateFilter = null);
    Task<Document> FormatDocumentAsync(Document document, CancellationToken cancellationToken);
    Task ApplyChangesAsync(Solution newSolution, CancellationToken cancellationToken);
    Task<Solution> FindAndReplaceAsync(string targetString, string regexPattern, string replacementText, CancellationToken cancellationToken, RegexOptions options = RegexOptions.Multiline);
}
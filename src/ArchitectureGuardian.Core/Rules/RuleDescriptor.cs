namespace ArchitectureGuardian.Core.Rules;

/// <summary>Describes a rule's stable identity and guidance independently of severity and evaluation logic.</summary>
public sealed record RuleDescriptor
{
    /// <summary>Creates the metadata shared by a rule and all findings it emits.</summary>
    /// <param name="id">The case-sensitive rule ID, using the canonical catalog for core rules.</param>
    /// <param name="title">The human-readable name of the architecture check.</param>
    /// <param name="documentationUrl">An absolute HTTP or HTTPS URL explaining the rule.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">The ID or title is blank, or the documentation URL is not absolute HTTP/HTTPS.</exception>
    public RuleDescriptor(string id, string title, Uri documentationUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(documentationUrl);
        if (!documentationUrl.IsAbsoluteUri || documentationUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Documentation must use an absolute HTTP or HTTPS URL.", nameof(documentationUrl));
        }

        Id = id;
        Title = title;
        DocumentationUrl = documentationUrl;
    }

    /// <summary>Gets the stable, case-sensitive identifier used for policy matching and diagnostic identity.</summary>
    public string Id { get; }

    /// <summary>Gets the human-readable name of the architecture check.</summary>
    public string Title { get; }

    /// <summary>Gets the absolute HTTP or HTTPS URL explaining the rule.</summary>
    public Uri DocumentationUrl { get; }
}

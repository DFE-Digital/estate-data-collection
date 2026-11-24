using System.Text.Json.Nodes;
using EstateDataCollection.Application.Abstraction;

namespace EstateDataCollection.Infrastructure.Validation.Enrichment;

/// <summary>
/// Populates payload-wide estate lookup collections (valid site, block, and evidence identifiers)
/// so quality rules across engines can verify cross-entity references without traversing the full tree repeatedly.
/// </summary>
public sealed class EstateAmbientReferenceEnricher : IRuleContextEnricher
{
    public void PopulateAmbientContext(JsonNode rootNode, IDictionary<string, JsonNode?> ambientContext)
    {
        var validSiteIds = new JsonArray();
        var validBlockIds = new JsonArray();
        var validEvidenceIds = new JsonArray();

        if (rootNode["sites"] is JsonArray sites)
        {
            foreach (var site in sites)
            {
                if (site?["id"]?.GetValue<string>() is { } siteId)
                {
                    validSiteIds.Add(siteId);
                }

                if (site?["name"]?.GetValue<string>() is { } siteName &&
                    !validSiteIds.Any(x => x?.GetValue<string>() == siteName))
                {
                    validSiteIds.Add(siteName);
                }

                if (site?["blocks"] is JsonArray blocks)
                {
                    foreach (var block in blocks)
                    {
                        if (block?["id"]?.GetValue<string>() is { } blockId)
                        {
                            validBlockIds.Add(blockId);
                        }

                        if (block?["name"]?.GetValue<string>() is { } blockName &&
                            !validBlockIds.Any(x => x?.GetValue<string>() == blockName))
                        {
                            validBlockIds.Add(blockName);
                        }
                    }
                }
            }
        }

        if (rootNode["evidence"] is JsonArray evidenceList)
        {
            foreach (var evidence in evidenceList)
            {
                if (evidence?["id"]?.GetValue<string>() is { } evId)
                {
                    validEvidenceIds.Add(evId);
                }
            }
        }

        ambientContext["valid_site_ids"] = validSiteIds;
        ambientContext["valid_block_ids"] = validBlockIds;
        ambientContext["valid_evidence_ids"] = validEvidenceIds;
    }
}

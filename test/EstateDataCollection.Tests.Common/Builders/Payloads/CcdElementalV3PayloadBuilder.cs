using System.Text.Json;
using System.Text.Json.Nodes;

namespace EstateDataCollection.Tests.Common.Builders.Payloads;

public sealed class CcdElementalV3PayloadBuilder
{
    public const string DefaultValidPayloadJson = """
    {
      "survey": {
        "id": "SURV-CCD-V3-001",
        "surveyName": "CCD Elemental Condition Survey v3",
        "participantId": "PART-SURV-991",
        "surveyorEmail": "lead.surveyor@education.gov.uk",
        "surveyDate": "2026-04-10",
        "isComplete": true,
        "createdAt": "2026-04-10T08:30:00Z",
        "updatedAt": "2026-04-10T15:45:00Z"
      },
      "school": {
        "urn": "104521",
        "name": "St Mary's Church of England Academy",
        "localAuthorityCode": "380"
      },
      "elements": [
        {
          "id": "ELM-001",
          "nrm3Code": "5.6.1",
          "productType": "Boiler",
          "elementCategory": "Mechanical",
          "assetType": "block",
          "siteId": "SITE-MAIN",
          "blockId": "BLK-A",
          "notAssessed": false,
          "occurrences": [
            {
              "id": "OCC-001",
              "surveyLevel": "Block",
              "occurrenceExtentOrLocationDescription": "Plant Room Ground Floor Boiler 1",
              "basisOfAssessment": "Seen",
              "conditionGrade": "D",
              "conditionScore": 18.5,
              "conditionDescription": "Primary condensing boiler shell shows heavy corrosion and valve leakage.",
              "priorityGrade": "4",
              "hasAsbestosSuspected": false,
              "photoReference": "PHOTO-BLK-A-BOILER-01.jpg",
              "assessedAt": "2026-04-10T10:15:00Z",
              "replacement": {
                "uom": "nr",
                "remLife": 2,
                "replacementQuantity": 5,
                "replacementRate": 1200.0,
                "replacementCost": 6000.0
              },
              "repairs": [
                {
                  "id": "REP-001",
                  "repairItem": "Boiler Valve",
                  "repairClassification": "X",
                  "repairDescription": "Replace corroded isolation valve assembly and pressure relief seal.",
                  "uom": "nr",
                  "repairQuantity": 3,
                  "repairItemRate": 0.1,
                  "year1Cost": 0.1,
                  "year2Cost": 0.2,
                  "year3Cost": 0.0,
                  "year4Cost": 0.0,
                  "year5Cost": 0.0,
                  "totalRepairCost": 0.3,
                  "urgentActionRequired": true
                }
              ]
            }
          ]
        },
        {
          "id": "ELM-002",
          "nrm3Code": "2.3.1",
          "productType": "Roof",
          "elementCategory": "Fabric",
          "assetType": "block",
          "siteId": "SITE-MAIN",
          "blockId": "BLK-B",
          "notAssessed": false,
          "occurrences": [
            {
              "id": "OCC-002",
              "surveyLevel": "Block",
              "occurrenceExtentOrLocationDescription": "South Wing Pitched Slate Roof",
              "basisOfAssessment": "Seen",
              "conditionGrade": "A",
              "conditionScore": 95.0,
              "conditionDescription": "Slate covering and ridge tiles intact with clean rainwater goods.",
              "priorityGrade": "1",
              "hasAsbestosSuspected": false,
              "assessedAt": "2026-04-10T11:20:00Z",
              "replacement": {
                "uom": "m2",
                "remLife": 25,
                "replacementQuantity": 100.0,
                "replacementRate": 85.0,
                "replacementCost": 8500.0
              }
            }
          ]
        },
        {
          "id": "ELM-003",
          "nrm3Code": "2.5.1",
          "productType": "Window",
          "elementCategory": "Fabric",
          "assetType": "floor",
          "siteId": "SITE-MAIN",
          "blockId": "BLK-B",
          "floorId": "FLR-B1",
          "notAssessed": true,
          "notAssessedReason": "External scaffolding prevented safe close-up window inspection during this visit."
        }
      ]
    }
    """;

    public const string DefaultAmbientJson = """
    {
      "cross_submission": {
        "identifierType": "URN",
        "identifierValue": "104521",
        "known_site_ids": [
          "SITE-MAIN",
          "SITE-ANNEX"
        ],
        "known_block_ids": [
          "BLK-A",
          "BLK-B",
          "BLK-C"
        ],
        "known_evidence_ids": [
          "EVD-001"
        ],
        "has_draft_references": false,
        "draft_submission_ids": [],
        "submissions": [
          {
            "submissionId": "11111111-1111-1111-1111-111111111111",
            "schemaName": "edc-survey-sites",
            "schemaVersion": 1,
            "status": "Finalised",
            "siteIds": [
              "SITE-MAIN",
              "SITE-ANNEX"
            ],
            "blockIds": [
              "BLK-A",
              "BLK-B",
              "BLK-C"
            ]
          }
        ]
      },
      "reference": {
        "product_types": [
          "Boiler",
          "Roof",
          "Window",
          "Electrical Distribution",
          "Fire Alarm",
          "External Paving"
        ],
        "nrm3_codes": [
          "2.3.1",
          "2.3.2",
          "2.5.1",
          "5.1.1",
          "5.6.1",
          "5.8.1",
          "8.1.1"
        ],
        "repair_items_by_product_type": {
          "Boiler": [
            "Boiler Valve",
            "Burner Assembly",
            "Flue Replacement",
            "Circulation Pump"
          ],
          "Roof": [
            "Roof Tile",
            "Lead Flashing",
            "Flat Roof Felt",
            "Gutter Replacement"
          ],
          "Window": [
            "Window Glazing Unit",
            "Window Frame",
            "Ironmongery"
          ]
        }
      }
    }
    """;

    private readonly JsonObject _root = JsonNode.Parse(DefaultValidPayloadJson)!.AsObject();

    public CcdElementalV3PayloadBuilder Mutate(Action<JsonObject> configure)
    {
        configure(_root);
        return this;
    }

    public JsonObject BuildNode() => _root.DeepClone().AsObject();

    public string BuildJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public static IReadOnlyDictionary<string, JsonNode?> BuildAmbientDictionary()
    {
        var parsed = JsonNode.Parse(DefaultAmbientJson)!.AsObject();
        var dict = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var kvp in parsed)
        {
            dict[kvp.Key] = kvp.Value?.DeepClone();
        }

        return dict;
    }

    public static IReadOnlyList<CcdElementalV3InvalidCaseDefinition> BuildInvalidCaseDefinitions() =>
    [
        new(
            "CCD-ELEM-V3-003 Critical timestamp inversion halts subsequent rule stages",
            ["CCD-ELEM-V3-003"],
            new Dictionary<string, JsonNode?>
            {
                ["survey.createdAt"] = JsonValue.Create("2026-04-12T10:00:00Z"),
                ["survey.updatedAt"] = JsonValue.Create("2026-04-10T08:00:00Z")
            }),
        new(
            "CCD-ELEM-V3-001 Unknown productType triggers DependsOn skip for CCD-ELEM-V3-016 and CCD-ELEM-V3-019",
            ["CCD-ELEM-V3-001"],
            new Dictionary<string, JsonNode?>
            {
                ["elements[0].productType"] = JsonValue.Create("UnknownHeatPump")
            }),
        new(
            "CCD-ELEM-V3-002 & CCD-ELEM-V3-020 Grade D missing photoReference and repairs",
            ["CCD-ELEM-V3-002", "CCD-ELEM-V3-020"],
            new Dictionary<string, JsonNode?>
            {
                ["elements[0].occurrences[0].photoReference"] = null,
                ["elements[0].occurrences[0].repairs"] = new JsonArray()
            }),
        new(
            "CCD-ELEM-V3-019 Repair item does not belong to grandparent Boiler productType",
            ["CCD-ELEM-V3-019"],
            new Dictionary<string, JsonNode?>
            {
                ["elements[0].occurrences[0].repairs[0].repairItem"] = JsonValue.Create("Roof Tile")
            }),
        new(
            "CCD-ELEM-V3-026 Repair quantity exceeds parent replacementQuantity when units match",
            ["CCD-ELEM-V3-026"],
            new Dictionary<string, JsonNode?>
            {
                ["elements[0].occurrences[0].replacement.replacementQuantity"] = JsonValue.Create(2),
                ["elements[0].occurrences[0].replacement.replacementCost"] = JsonValue.Create(2400.0)
            }),
        new(
            "CCD-ELEM-V3-048 & CCD-ELEM-V3-049 Real arithmetic mismatch (0.001) is not masked by float epsilon",
            ["CCD-ELEM-V3-048", "CCD-ELEM-V3-049"],
            new Dictionary<string, JsonNode?>
            {
                ["elements[0].occurrences[0].repairs[0].totalRepairCost"] = JsonValue.Create(0.301)
            })
    ];
}

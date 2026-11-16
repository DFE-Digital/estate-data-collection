namespace EstateDataCollection.Tests.Common.Fixtures;

public static class CdePayloadFixtures
{
    public const string ValidSchoolSitePayload = """
    {
      "survey": {
        "id": "SURV-2026-001",
        "survey_name": "CDC2 School Site Survey",
        "participant_id": "PART-100",
        "surveyor_email": "surveyor@education.gov.uk",
        "is_complete": true,
        "created_at": "2026-03-25T09:00:00Z",
        "updated_at": "2026-03-25T11:30:00Z"
      },
      "school": {
        "urn": "123456",
        "name": "Oakwood Academy",
        "local_authority": "Camden"
      },
      "sites": [
        {
          "name": "Main Campus",
          "blocks": [
            {
              "name": "Block A",
              "type": "Teaching",
              "floors": [
                { "name": "Ground Floor" },
                { "name": "First Floor" }
              ]
            }
          ]
        }
      ]
    }
    """;

    public const string InvalidTimestampSchoolSitePayload = """
    {
      "survey": {
        "id": "SURV-2026-001",
        "survey_name": "CDC2 School Site Survey",
        "participant_id": "PART-100",
        "surveyor_email": "surveyor@education.gov.uk",
        "is_complete": true,
        "created_at": "2026-03-25T15:00:00Z",
        "updated_at": "2026-03-25T10:00:00Z"
      },
      "school": {
        "urn": "123456",
        "name": "Oakwood Academy",
        "local_authority": null
      },
      "sites": [
        {
          "name": "Main Campus",
          "blocks": [
            {
              "name": "Block A",
              "type": "Teaching",
              "floors": null
            }
          ]
        }
      ]
    }
    """;

    public const string ValidSurveyElementsPayload = """
    {
      "survey": {
        "id": "SURV-2026-002",
        "survey_name": "CDC2 Elements Survey",
        "participant_id": "PART-101",
        "surveyor_email": null,
        "is_complete": true,
        "created_at": "2026-03-25T09:00:00Z",
        "updated_at": "2026-03-25T09:00:00Z"
      },
      "school": {
        "urn": "654321",
        "name": "Riverbank Primary"
      },
      "elements": [
        {
          "id": "ELM-001",
          "NRM3_code": "2.1.1",
          "description": "Flat Roof Structure",
          "asset_id": "Block A",
          "asset_type": "block",
          "occurrences": [
            {
              "id": "OCC-001",
              "name": "Main Flat Roof",
              "basis_of_assessment": "Seen",
              "condition_grade": "D",
              "condition_description": "Severe membrane degradation",
              "replacement": {
                "rem_life": 2,
                "uom": "m2",
                "replacement_quantity": 120.5,
                "replacement_rate": 150.0
              },
              "repairs": [
                {
                  "id": "REP-001",
                  "repair_description": "Replace roof covering",
                  "repair_item": "Bitumen felt",
                  "repair_classification": "X",
                  "uom": "m2",
                  "repair_quantity": 40,
                  "repair_rate": 200,
                  "estimated_cost": 8000
                }
              ]
            }
          ]
        }
      ]
    }
    """;

    public const string ValidEvidenceAndAnswersPayload = """
    {
      "survey": {
        "id": "SURV-2026-003",
        "survey_name": "CDC2 Answers & Evidence Survey",
        "participant_id": "PART-102",
        "surveyor_email": "inspector@education.gov.uk",
        "is_complete": false,
        "created_at": "2026-03-20T08:00:00Z",
        "updated_at": "2026-03-21T16:45:00Z"
      },
      "school": {
        "urn": "112233",
        "name": "Hilltop High"
      },
      "answers": [
        {
          "id": "ANS-001",
          "question_id": "Q-RAAC-01",
          "asset_id": "Block A",
          "asset_type": "block",
          "value": "false",
          "value_type": "boolean",
          "created_at": "2026-03-20T09:00:00Z",
          "updated_at": "2026-03-20T09:05:00Z",
          "evidence": [
            { "id": "EVD-001" }
          ]
        }
      ],
      "evidence": [
        {
          "id": "EVD-001",
          "file_name": "roof-inspection.jpg",
          "file_path": "/evidence/2026/roof-inspection.jpg",
          "mime_type": "image/jpeg"
        }
      ]
    }
    """;
}

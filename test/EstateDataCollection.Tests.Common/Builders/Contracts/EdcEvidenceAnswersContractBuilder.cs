namespace EstateDataCollection.Tests.Common.Builders.Contracts;

public sealed class EdcEvidenceAnswersContractBuilder
{
    public const string DefaultSchemaJson = """
    {
      "$schema": "http://json-schema.org/draft-07/schema#",
      "type": "object",
      "properties": {
        "survey": {
          "type": "object",
          "properties": {
            "id": {
              "type": "string",
              "pattern": "^SURV-[A-Za-z0-9-]+$"
            },
            "survey_name": {
              "type": "string"
            },
            "participant_id": {
              "type": "string"
            },
            "surveyor_email": {
              "type": [
                "string",
                "null"
              ],
              "format": "email"
            },
            "is_complete": {
              "type": "boolean"
            },
            "created_at": {
              "type": "string",
              "format": "date-time"
            },
            "updated_at": {
              "type": "string",
              "format": "date-time"
            }
          },
          "required": [
            "id",
            "survey_name",
            "participant_id",
            "is_complete",
            "created_at",
            "updated_at"
          ]
        },
        "school": {
          "type": "object",
          "properties": {
            "urn": {
              "type": "string"
            },
            "name": {
              "type": [
                "string",
                "null"
              ]
            }
          },
          "required": [
            "urn"
          ]
        },
        "answers": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": {
                "type": "string",
                "pattern": "^ANS-[0-9]+$"
              },
              "question_id": {
                "type": "string"
              },
              "asset_id": {
                "type": "string"
              },
              "asset_type": {
                "type": "string",
                "enum": [
                  "site",
                  "block",
                  "floor",
                  "space",
                  "element"
                ]
              },
              "value": {
                "type": "string",
                "description": "Survey response value. Actual datatype is determined by value_type."
              },
              "value_type": {
                "type": [
                  "string",
                  "null"
                ],
                "enum": [
                  "text",
                  "number",
                  "boolean",
                  "lookup_list",
                  "date",
                  null
                ]
              },
              "created_at": {
                "type": [
                  "string",
                  "null"
                ],
                "format": "date-time"
              },
              "updated_at": {
                "type": [
                  "string",
                  "null"
                ],
                "format": "date-time"
              },
              "evidence": {
                "type": [
                  "array",
                  "null"
                ],
                "items": {
                  "type": [
                    "object",
                    "null"
                  ],
                  "properties": {
                    "id": {
                      "type": "string",
                      "pattern": "^EVD-[0-9]+$"
                    }
                  },
                  "required": [
                    "id"
                  ]
                }
              }
            },
            "required": [
              "id",
              "question_id",
              "asset_id",
              "asset_type",
              "value"
            ]
          }
        },
        "evidence": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": {
                "type": "string",
                "pattern": "^EVD-[0-9]+$"
              },
              "file_name": {
                "type": [
                  "string",
                  "null"
                ]
              },
              "file_path": {
                "type": [
                  "string",
                  "null"
                ]
              },
              "mime_type": {
                "type": [
                  "string",
                  "null"
                ]
              }
            },
            "required": [
              "id"
            ]
          }
        }
      },
      "required": [
        "survey",
        "school",
        "answers",
        "evidence"
      ],
      "description": "Top-level CCDSurveySubmission payload container"
    }
    """;

    public const string DefaultRulesJson = EdcSchoolSiteContractBuilder.DefaultRulesJson;

    public const string DefaultOdcsYaml = """
    version: "1.0.0"
    kind: DataContract
    apiVersion: v3.2.0

    id: edc-survey-answers-evidence
    name: CCDSurveySubmission-AnswersEvidence
    status: draft

    domain: capital-delivery
    tenant: DfE

    servers:
      - server: databricks
        type: databricks
        environment: dev
        description: CCD Survey answers and evidence data stored in the Databricks Silver layer
        catalog: eed_catalogue_30_bronze
        schema: cde_edc_integration

    description:
      purpose: >
        Captures school estate survey submissions including survey metadata,
        school details, survey answers, and supporting evidence files.
      usage: >
        Used for collection, validation, auditability, reporting,
        and downstream processing of survey responses.
      limitations: >
        Only supports the structures defined within the survey answers
        and evidence schema. Additional properties are not permitted.
      customProperties:
        - property: catalog
          value: eed_cdc2_geospatial
        - property: schema
          value: silver
        - property: storageFormat
          value: delta

    schema:
      - name: submission
        logicalType: object
        description: Top-level CCDSurveySubmission payload container
        properties:
          - name: survey
            logicalType: object
            required: true
            quality:
              - id: CDC-DQ1-JSONLogic
                type: custom
                engine: jsonlogic
                description: Survey updated timestamp must be greater than or equal to survey creation timestamp.
                implementation:
                  ">=":
                    - var: survey.updated_at
                    - var: survey.created_at
            properties:
              - name: id
                physicalType: string
                logicalType: string
                required: true
                logicalTypeOptions:
                  pattern: "^SURV-[A-Za-z0-9-]+$"
              - name: survey_name
                physicalType: string
                logicalType: string
                required: true
              - name: participant_id
                physicalType: string
                logicalType: string
                required: true
              - name: surveyor_email
                physicalType: string
                logicalType: string
                required: false
                logicalTypeOptions:
                  format: email
              - name: is_complete
                physicalType: boolean
                logicalType: boolean
                required: true
              - name: created_at
                physicalType: string
                logicalType: timestamp
                required: true
              - name: updated_at
                physicalType: string
                logicalType: timestamp
                required: true

          - name: school
            logicalType: object
            required: true
            properties:
              - name: urn
                physicalType: string
                logicalType: string
                required: true
              - name: name
                physicalType: string
                logicalType: string
                required: false

          - name: answers
            logicalType: array
            required: true
            items:
              logicalType: object
              required: true
              properties:
                - name: id
                  physicalType: string
                  logicalType: string
                  required: true
                  logicalTypeOptions:
                    pattern: "^ANS-[0-9]+$"
                - name: question_id
                  physicalType: string
                  logicalType: string
                  required: true
                - name: asset_id
                  physicalType: string
                  logicalType: string
                  required: true
                - name: asset_type
                  physicalType: string
                  logicalType: string
                  required: true
                  enum:
                    - value: site
                    - value: block
                    - value: floor
                    - value: space
                    - value: element
                - name: value
                  logicalType: string
                  required: true
                  description: Survey response value. Actual datatype is determined by value_type.
                - name: value_type
                  physicalType: string
                  logicalType: string
                  required: false
                  enum:
                    - value: text
                    - value: number
                    - value: boolean
                    - value: lookup_list
                    - value: date
                - name: created_at
                  physicalType: string
                  logicalType: timestamp
                  required: false
                - name: updated_at
                  physicalType: string
                  logicalType: timestamp
                  required: false
                - name: evidence
                  logicalType: array
                  required: false
                  items:
                    logicalType: object
                    properties:
                      - name: id
                        physicalType: string
                        logicalType: string
                        required: true
                        logicalTypeOptions:
                          pattern: "^EVD-[0-9]+$"

          - name: evidence
            logicalType: array
            required: true
            items:
              logicalType: object
              required: true
              properties:
                - name: id
                  physicalType: string
                  logicalType: string
                  required: true
                  logicalTypeOptions:
                    pattern: "^EVD-[0-9]+$"
                - name: file_name
                  physicalType: string
                  logicalType: string
                  required: false
                - name: file_path
                  physicalType: string
                  logicalType: string
                  required: false
                - name: mime_type
                  physicalType: string
                  logicalType: string
                  required: false

    team:
      name: CDC2 Geospatial Programme
      members:
        - username: data-owner
          role: owner

    tags:
      - cdc2
      - geospatial
      - school-estate
      - survey
      - answers
      - evidence
      - audit
    """;

    public string BuildSchemaJson() => DefaultSchemaJson;

    public string BuildRulesJson() => DefaultRulesJson;

    public string BuildOdcsYaml() => DefaultOdcsYaml;
}

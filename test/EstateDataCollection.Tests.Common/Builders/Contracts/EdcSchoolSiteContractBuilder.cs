namespace EstateDataCollection.Tests.Common.Builders.Contracts;

public sealed class EdcSchoolSiteContractBuilder
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
              "type": "string",
              "pattern": "^[0-9]{6}$"
            },
            "name": {
              "type": "string"
            },
            "local_authority": {
              "type": [
                "string",
                "null"
              ]
            }
          },
          "required": [
            "urn",
            "name"
          ]
        },
        "sites": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "name": {
                "type": "string"
              },
              "blocks": {
                "type": "array",
                "items": {
                  "type": "object",
                  "properties": {
                    "name": {
                      "type": "string"
                    },
                    "type": {
                      "type": "string"
                    },
                    "floors": {
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
                          "name": {
                            "type": "string"
                          }
                        },
                        "required": [
                          "name"
                        ]
                      }
                    }
                  },
                  "required": [
                    "name",
                    "type"
                  ]
                }
              }
            },
            "required": [
              "name",
              "blocks"
            ]
          }
        }
      },
      "required": [
        "survey",
        "school",
        "sites"
      ],
      "description": "Top-level CCDSurveySubmission-Sites payload container"
    }
    """;

    public const string DefaultRulesJson = """
    [
      {
        "id": "CDC-DQ1-JSONLogic",
        "type": "custom",
        "engine": "jsonlogic",
        "description": "Survey updated timestamp must be greater than or equal to survey creation timestamp.",
        "severity": "ERROR",
        "path": "survey",
        "message": "Survey updated timestamp must be greater than or equal to survey creation timestamp.",
        "implementation": {
          ">=": [
            {
              "var": "survey.updated_at"
            },
            {
              "var": "survey.created_at"
            }
          ]
        }
      }
    ]
    """;

    public const string DefaultOdcsYaml = """
    version: "1.0.0"
    kind: DataContract
    apiVersion: v3.2.0

    id: edc-survey-sites
    name: CCDSurveySubmission-Sites
    status: draft

    domain: capital-delivery
    tenant: DfE

    servers:
      - server: databricks
        type: databricks
        environment: dev
        description: CCD Survey data stored in the Databricks Silver layer
        catalog: eed_catalogue_30_bronze
        schema: cde_edc_integration

    description:
      purpose: >
        Captures school estate survey submissions, including survey metadata,
        school details, sites, blocks, and floors.
      usage: >
        Used for collection, validation, and downstream processing of school
        site survey information.
      limitations: >
        Only supports the structures defined within the survey submission schema.
        Additional properties are not permitted.
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
        description: Top-level CCDSurveySubmission-Sites payload container
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
                logicalTypeOptions:
                  pattern: "^[0-9]{6}$"
              - name: name
                physicalType: string
                logicalType: string
                required: true
              - name: local_authority
                physicalType: string
                logicalType: string
                required: false

          - name: sites
            logicalType: array
            required: true
            items:
              logicalType: object
              required: true
              properties:
                - name: name
                  physicalType: string
                  logicalType: string
                  required: true
                - name: blocks
                  logicalType: array
                  required: true
                  items:
                    logicalType: object
                    required: true
                    properties:
                      - name: name
                        physicalType: string
                        logicalType: string
                        required: true
                      - name: type
                        physicalType: string
                        logicalType: string
                        required: true
                      - name: floors
                        logicalType: array
                        required: false
                        items:
                          logicalType: object
                          properties:
                            - name: name
                              physicalType: string
                              logicalType: string
                              required: true

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
      - sites
    """;

    public string BuildSchemaJson() => DefaultSchemaJson;

    public string BuildRulesJson() => DefaultRulesJson;

    public string BuildOdcsYaml() => DefaultOdcsYaml;
}

namespace EstateDataCollection.Tests.Common.Builders.Contracts;

public sealed class EdcSurveyElementsContractBuilder
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
        "elements": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": {
                "type": "string",
                "pattern": "^ELM-[0-9]+$"
              },
              "NRM3_code": {
                "type": "string"
              },
              "description": {
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
                  "space"
                ]
              },
              "occurrences": {
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
                      "pattern": "^OCC-[0-9]+$"
                    },
                    "name": {
                      "type": "string"
                    },
                    "basis_of_assessment": {
                      "type": [
                        "string",
                        "null"
                      ],
                      "enum": [
                        "Seen",
                        "Not Seen",
                        "Assumed",
                        null
                      ]
                    },
                    "condition_grade": {
                      "type": [
                        "string",
                        "null"
                      ],
                      "enum": [
                        "A",
                        "B",
                        "C",
                        "D",
                        null
                      ]
                    },
                    "condition_description": {
                      "type": [
                        "string",
                        "null"
                      ]
                    },
                    "replacement": {
                      "type": [
                        "object",
                        "null"
                      ],
                      "properties": {
                        "rem_life": {
                          "type": [
                            "integer",
                            "null"
                          ]
                        },
                        "uom": {
                          "type": [
                            "string",
                            "null"
                          ]
                        },
                        "replacement_quantity": {
                          "type": [
                            "number",
                            "null"
                          ]
                        },
                        "replacement_rate": {
                          "type": [
                            "number",
                            "null"
                          ]
                        }
                      },
                      "required": []
                    },
                    "repairs": {
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
                            "pattern": "^REP-[0-9]+$"
                          },
                          "repair_description": {
                            "type": [
                              "string",
                              "null"
                            ]
                          },
                          "repair_item": {
                            "type": [
                              "string",
                              "null"
                            ]
                          },
                          "repair_classification": {
                            "type": [
                              "string",
                              "null"
                            ],
                            "enum": [
                              "X",
                              "Y",
                              "Z",
                              null
                            ]
                          },
                          "uom": {
                            "type": [
                              "string",
                              "null"
                            ]
                          },
                          "repair_quantity": {
                            "type": [
                              "number",
                              "null"
                            ]
                          },
                          "repair_rate": {
                            "type": [
                              "number",
                              "null"
                            ]
                          },
                          "estimated_cost": {
                            "type": [
                              "number",
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
                    "id",
                    "name"
                  ]
                }
              }
            },
            "required": [
              "id",
              "NRM3_code",
              "description",
              "asset_id",
              "asset_type"
            ]
          }
        }
      },
      "required": [
        "survey",
        "school",
        "elements"
      ],
      "description": "Top-level CCDSurveySubmission payload container"
    }
    """;

    public const string DefaultRulesJson = EdcSchoolSiteContractBuilder.DefaultRulesJson;

    public const string DefaultOdcsYaml = """
    version: "1.0.0"
    kind: DataContract
    apiVersion: v3.2.0

    id: edc-survey-elements
    name: CCDSurveySubmission-Elements
    status: draft

    domain: capital-delivery
    tenant: DfE

    servers:
      - server: databricks
        type: databricks
        environment: dev
        description: CCD Survey element condition and repair data stored in the Databricks Silver layer
        catalog: eed_catalogue_30_bronze
        schema: cde_edc_integration

    description:
      purpose: >
        Captures school estate survey submissions including survey metadata,
        school details, surveyed elements, occurrences, replacement costs,
        and repair requirements.
      usage: >
        Used for collection, validation, asset condition assessment,
        lifecycle planning, and downstream reporting.
      limitations: >
        Only supports the structures defined within the survey elements schema.
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

          - name: elements
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
                    pattern: "^ELM-[0-9]+$"
                - name: NRM3_code
                  physicalType: string
                  logicalType: string
                  required: true
                - name: description
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
                - name: occurrences
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
                          pattern: "^OCC-[0-9]+$"
                      - name: name
                        physicalType: string
                        logicalType: string
                        required: true
                      - name: basis_of_assessment
                        physicalType: string
                        logicalType: string
                        required: false
                        enum:
                          - value: Seen
                          - value: Not Seen
                          - value: Assumed
                      - name: condition_grade
                        physicalType: string
                        logicalType: string
                        required: false
                        enum:
                          - value: A
                          - value: B
                          - value: C
                          - value: D
                      - name: condition_description
                        physicalType: string
                        logicalType: string
                        required: false
                      - name: replacement
                        logicalType: object
                        required: false
                        properties:
                          - name: rem_life
                            physicalType: integer
                            logicalType: integer
                            required: false
                          - name: uom
                            physicalType: string
                            logicalType: string
                            required: false
                          - name: replacement_quantity
                            physicalType: number
                            logicalType: number
                            required: false
                          - name: replacement_rate
                            physicalType: number
                            logicalType: number
                            required: false
                      - name: repairs
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
                                pattern: "^REP-[0-9]+$"
                            - name: repair_description
                              physicalType: string
                              logicalType: string
                              required: false
                            - name: repair_item
                              physicalType: string
                              logicalType: string
                              required: false
                            - name: repair_classification
                              physicalType: string
                              logicalType: string
                              required: false
                              enum:
                                - value: X
                                - value: Y
                                - value: Z
                            - name: uom
                              physicalType: string
                              logicalType: string
                              required: false
                            - name: repair_quantity
                              physicalType: number
                              logicalType: number
                              required: false
                            - name: repair_rate
                              physicalType: number
                              logicalType: number
                              required: false
                            - name: estimated_cost
                              physicalType: number
                              logicalType: number
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
      - elements
      - condition
      - repairs
    """;

    public string BuildSchemaJson() => DefaultSchemaJson;

    public string BuildRulesJson() => DefaultRulesJson;

    public string BuildOdcsYaml() => DefaultOdcsYaml;
}

namespace CopilotLedger.Tests.TestSupport;

public static class RateFixtures
{
    public const string ByokTestRatesJson = """
        {
          "schemaVersion": 1,
          "ratesVersion": "test-fixture",
          "source": { "url": "https://example.invalid/rates", "retrievedOn": "2026-11-20" },
          "notes": ["Fictional rates for tests only."],
          "githubAiCredits": {
            "hostSuffixes": ["githubcopilot.com"],
            "models": [
              {
                "id": "claude-sonnet-5.5",
                "aliases": [],
                "windows": [
                  {
                    "effectiveFrom": null,
                    "effectiveUntil": null,
                    "tiers": [
                      { "name": "default", "maxInputTokens": null, "inputPer1M": 2.00, "cachedInputPer1M": 0.10, "cacheWritePer1M": 2.50, "outputPer1M": 10.00 }
                    ]
                  }
                ]
              }
            ]
          },
          "foundryByok": {
            "hostSuffixes": ["services.ai.azure.com", "openai.azure.com", "cognitiveservices.azure.com"],
            "providerNames": ["azure.ai.inference"],
            "models": [
              {
                "id": "gpt-6.1-sol",
                "aliases": [],
                "windows": [
                  {
                    "effectiveFrom": null,
                    "effectiveUntil": null,
                    "tiers": [
                      { "name": "default", "maxInputTokens": null, "inputPer1M": 1.75, "cachedInputPer1M": 0.175, "cacheWritePer1M": null, "outputPer1M": 14.00 }
                    ]
                  }
                ]
              }
            ]
          },
          "localOffload": {
            "baselineModel": "claude-sonnet-5.5",
            "models": [],
            "additionalHosts": [],
            "providerNames": []
          }
        }
        """;
}

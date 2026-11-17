# Implementing chaos strategies into a resilience pipeline

## What was built?

- A simple application that allows chaos strategies to be added into a resilience pipeline.
- Expanding upon the existing resilience pipeline to implement chaos strategies for each service.

## Why this matters?

- Resilience policies are only trustworthy if they have been exercised. Chaos injection allows you to observe and prove the policies implemented are working as expected.
- Simmy runs in-process inside the resilience pipelines ensuring failures can be injected without standing up emulated endpoints, proxies or touching infrastructure.
- Chaos is expressed as pipeline configuration allowing for replayable and deterministic tests.
- Compliments the emulated integration approach. Emulated endpoints exercise the full HTTP round trip while Simmy can target a specific strategy in isolation with precise control rate and the type of failure.

## What the spike achieved?

The application shows how chaos strategies can be layered directly into an existing Polly resilience pipeline. Each strategy can be configured independently for injection rate and failure type. As chaos sits within the pipeline, the retries, timeouts, and circuit breakers are the components reacting to the injected failures which makes their behaviour directly observable and verifiable. Simmy provides a controlled, in-process way to produce faults, latency and fake outcomes on demand without needing emulated endpoints or real downstream instability.

Simmy offers four default chaos strategies:

| Strategy | Description |
| --- | --- |
| Fault | Injects exceptions in your system. |
| Outcome | Injects fake outcomes (results or execptions) into your system. |
| Latency | Injects latency into executions before the calls are made. |
| Behavior | Injects any extra behavior before a call is made. |

Each strategy shares a common set of options; an `InjectionRate` (probability from 0 to 1 inclusive), an `Enabled` flag as well as generator overloads that allow both to be resolved dynamically at runtime.

Custom chaos strategies can be defined by utilising the `Behavior` strategy. This proactive strategy is designed to inject custom behavior into the system before an operation is invoked. This allows users to alter inputs, simulate resource exhaustion or put the system in a given state before the operation. The `Behavior` strategy has limitations however as it can only put the system into a certain state before or around the call.

If the `Behavior` strategy is not sufficient then custom strategies can be implemented but requires additional configuration.

## Limitations and risks

As Simmy is implemented directly within the application careful consideration of the chaos strategies and the logic surrounding them must be considered to limit the blast radius of unintended strategy injection. Accidentally leaving high intensity chaos strategies within a production environment can cause widespread outages. However testing solely within the lower environments, dev, test, staging etc, can cause environmental skew.

Failures could cascade with multiple high intensity strategies being invoked at once. Injecting severe latency or faults could inadvertently overwhelm upstream systems that lack sufficient timeout or rate limiting protections. This could be compounded if the chaos strategies have not been configured with the correct settings enabling the detection of when a strategy has been invoked. Therefore potentially increasing the time taken debugging issues.

Simmy is limited to the four base strategies and requires additional configuration for other custom chaos strategies.

## Mitigation

- Isolate tests to specific environments using configuration or feature flags. This is to prevent Simmy running or limit the intensity of the strategies within production environments.
- Introduce faults gradually. Start with internal-only services or a small percentage of traffic.
- Pair Simmy with a more robust logging and monitoring platform/ application for better fault injection detection.

## Suggested next steps

1. Build the Simmy-driven test suite with paramaterised tests.
2. Define a standard chaos configuration convention before it is used more widely. This should include the injection rate limits for each environment, enabled by default on each environment, naming conventions, etc.
3. Wire in callbacks into the same telemetry explored within Spike B so injected chaos is correctly labelled and more easily distinguished from a genuine incident.

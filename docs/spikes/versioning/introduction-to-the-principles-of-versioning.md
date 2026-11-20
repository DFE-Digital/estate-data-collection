# Introduction to the principles of Versioning

API Versioning is the practice of managing changes to your API over time in a way that allows you to improve your system without breaking the applications that rely on it.

At its core, versioning is about **trust**. When developers integrate with your API, they trust that their integration will continue to work tomorrow exactly as it works today.

Here are the foundational principles of API versioning.

## Hyrum’s Law and the Inevitability of Change

When a software system or library becomes widely used, consumers will naturally write code that relies on every observable quirk, side effect, or implementation detail.

Hyrum’s Law has a really good statement on this

> "With a sufficient number of users of an API, it does not matter what you promise in the contract: all observable behaviors of your system will be depended on by somebody."

APIs are never truly “finished” like any software product. Business requirements change, security patches are needed, new features are requested, downstream packages update and many other factors.

Because clients will depend on every quirk and exact shape of your responses, any modification you make carries the risk of breaking someone’s integration. Versioning is the mechanism we use to making that risk safely, as the above examples are common product lifecycles even on products being subset.

## The Lifecycle of a API

An API version is never expected to live forever but it shouldn’t disappear overnight, like mentioned before. A well-managed API follows a strict lifecycle states.

1. **Active**: The current recommended version.
2. **Supported**: Older versions that are no longer recommend for new integration, but are still actively maintained via security and bug fixes.
3. **Deprecated**: The version is scheduled for removal. A clear “Sunset” date should be communicated to consumers (at least six months before) so they have time co migrate.
4. **Sunset** (or Retired): The version is turned off and any requests return a `404 Not Found` or `410 Gone` response with header information pointing to a sunset plan.

## Defining the difference of Additive vs. Breaking Changes

Understanding the difference between these two types of changes is the most critical skill in API design, as it’s the most common mistake done where teams either over increment or stay on the same version for too long.

### Additive Changes

Or non-breaking changes, these are **safe** changes. They typically do not require a new version as older clients / implementations change just ignore the new information.

Always think these example as **new** changes:

- **Adding a new endpoint** (eg. creating a `/api/v1/invoices` endpoint);
- **Adding a new optional field** to the request payload;
- **Adding a new field** to a response payload.

As mentioned, see the common theme of “add” or “new”, when nothing truly changes then a new version is not needed.

### Breaking Changes

Conversely, these are unsafe changes. Here are a few examples of when it is recommended to create a new version.

Always think of these example as your **changing something already in use**:

- **Removing or renaming a field** or endpoint;
- **Changing the data type** of a field (eg. from a string to boolean);
- **Adding a new required field** to the request payload;
- **Changing the validation rules to be more strict**;
- **Removing a value from an enum property** (or even adding one, if the client doesn’t handle unknown values it can cause failures).

The common theme this time are is anything changing of existing logic or is something required which is being added.

## When to Version

There are generally two philosophies on how to label your versions.

### Semantic Versioning

Or SemVer which is the typically `MAJOR-MINOR-PATCH` approach: `v1`, `v2`, `v3`, and so on. Typically the endpoints themselves just attach to the major, so `/api/v1/invoices`, whilst any minor or patch is just naturally consumed, your application’s assembly or information version could increase but that’s more infrastructure concerns.

A new version typically indicates a massive structural overhaul to the api.

| Pros | Cons |
| --- | --- |
| Simple and clean approach | Migrations between versions are typically huge and painful projects for the consumer as models and logic can change drastically all at once |

This style of versioning is good for your packages or SDKs that are consumed by your applications.

### Calendar Version

Is what it states on the tin, its date based: `2026-06-28`, `2026-07-01`, and so on. Is where it ties version tags directly to the release date instead of API change scopes. Common naming patterns include `YYYY-MM`, `YYYY-MM-DD` or `YY-MM-PATCH`, making it clear when a build was published.

A new version is created for *any* breaking change, no matter how small.

| Pros | Cons |
| --- | --- |
| Upgrades are incremental and easy for clients | Requires additional tooling to manage simultaneous versions |
| You can fix small design mistakes rapidly | |

This style of versioning is good for your modern APIs, presentation based applications.

### Something to note

> [!NOTE]
> This understanding helped shape the recommendation of which approach to use to a given scenario.

There seems to have been a shift to CalVer on endpoints as modern Saas companies like Stripe, OpenAi and others have migrated to this. Stripe uses a date-based approach plus a named cadence, with an internal process helping translating/migrating all the way back down the API version history.

Another clever thing they do with their clients, when a client begins an integration they are defaulted to a given version when they sign up and are pinned to that version until they explicitly upgrade. Also, if you consume their SDK packages it again is defaulted to the current API version which was Active at the time the SDK was produced.

For more information on this topic a good introduction by ByteMonk on Youtube is a good watch: [Why Stripe’s API Never Breaks | Date-Based Versioning Explained](https://tinyurl.com/4fvh42s7).

## Backwards Compatibility is Sacred

There’s a golden rule of versioning, which is to never break a running client.

If a client is integrated with a given version of your API and you have not announced a deprecation date then a request that worked on say version `2024-06-01` should still work like the latest `2026-08-01` version, yielding the exact same shape of data, regardless if the database and models look completely different two years later.

This is where API management can become complex needing multiple moving parts to keep all versions of data inline via background services and stored procedures.

## Summary

Good versioning means you can move fast internally while appearing perfectly stable externally. It shifts the burden of dealing with change from your users (who would otherwise experience broken apps) to your backend (which must maintain compatibility layers and translators).

## Further readings

An interesting read is about Romantic Versioning (RomVer), which can be found here: [Why Semantic Versioning Isn't](https://gist.github.com/jashkenas/cbd2b088e20279ae2c8e).

The TLDR of it is a satirical or pragmatic software versioning scheme that humorously mocks strict Semantic Versioning by basing version numbers on real-world chaotic usage, personal attachment to numbers, and intuitive milestones rather than rigid API rules.

A good example of this would be to take Microsoft Windows, the marketing version could be:

- Windows 7, but the internal assembly version could be SemVer of `6.1`;
- or. lets take Windows 11 they changed to `YYHX`, which is nicknamed WinVer, which is `YY` is year and `X` is the first or second half of the year.

## Other items

Here is a list of items which are pretty good for further reading:

1. [What is API versioning? Benefits, types & best practices | Postman](https://www.postman.com/api-platform/api-versioning/)
2. API versioning
3. [Hyrum's Law | Laws of Software Engineering](https://www.laws-of-software.com/laws/hyrum/)

For further reading into how this all works within the dotnet space continue here: [Versioning within .Net](./versioning-within-dotnet.md).

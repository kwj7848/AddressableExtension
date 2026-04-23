# Addressable Extension

A Unity package for reducing manual Addressables metadata maintenance.

Instead of treating Addressable addresses and labels as hand-maintained tracked metadata, this package standardizes them into a reproducible form and generates strongly-typed C# accessors via a Roslyn Source Generator.

This helps teams avoid manually managing string-based Addressables state in Git, keep naming consistent, and use addresses and labels from code without relying on raw strings.

## Why This Exists

In many Unity projects, Addressable addresses and labels gradually become hand-maintained metadata:

- They are edited manually
- They drift across contributors
- They create unnecessary review and merge overhead
- They are often consumed from code as fragile raw strings

Addressable Extension is built around a different approach: addresses and labels should be normalized by rule, treated as reproducible derived state where possible, and exposed to code through compile-time generated C# accessors.

## How Generated Code Works

Addressable Extension does not write generated `.cs` files into your project.

`AddressableNames` and `AddressableLabels` are emitted at compile time by a Roslyn Source Generator. They are available to your C# code and IDE autocomplete, but the generated source itself is not stored as hand-maintained source in your repository.

This keeps generated Addressables accessors out of Git while still giving you strongly-typed, autocomplete-friendly constants in code.

## Requirements

- Unity 2021.3 or later
- Addressables package 1.19.0 or later

## Installation

Install via Unity Package Manager using a Git URL:

1. Open **Window > Package Manager**
2. Click the **+** button (top-left) > **Add package from git URL...**
3. Enter the following URL:

```text
https://github.com/kwj7848/AddressableExtension.git?path=Packages/com.jeanlab.addressable-extension
```

## Usage

### Generating Keys

1. Open **Window > Asset Management > Addressables Groups**.
2. Click the **Generate Keys** button at the bottom-right corner.
3. A blue button indicates pending changes. Hover over it to see a tooltip listing added/removed items.

Also available from the menu: **Window > Asset Management > Addressables Extension > Generate Keys**

### Using Generated Code

```csharp
// Using an address constant
Addressables.LoadAssetAsync<GameObject>(AddressableExtension.AddressableNames.MyPrefab);

// Using a label constant
Addressables.LoadAssetsAsync<Object>(AddressableExtension.AddressableLabels.MyLabel, null);
```

### Settings

Open **Window > Asset Management > Addressables Extension > Settings**:

- **Generate AddressableNames**: String constants for all Addressable addresses, enabling code autocomplete. Also auto-simplifies new addresses.
- **Generate AddressableLabels**: String constants for all Addressable labels, enabling code autocomplete. Also auto-sanitizes new labels.

## Repository Structure

```text
Packages/
├── com.jeanlab.addressable-extension/            # UPM package (installed via Package Manager)
└── com.jeanlab.addressable-extension.generator/  # Source Generator source code (reference only)
```

## License

[MIT](Packages/com.jeanlab.addressable-extension/LICENSE.md)

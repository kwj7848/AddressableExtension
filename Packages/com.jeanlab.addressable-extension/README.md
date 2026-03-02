# Addressable Extension

A Unity package that auto-generates C# constants from Addressable asset Addresses and Labels.
Uses a Source Generator to create `AddressableNames` and `AddressableLabels` classes at compile time, enabling code autocomplete and eliminating string typos when working with Addressables.

## Requirements

- Unity 2021.3 or later
- Addressables package 1.19.0 or later

## Installation

Install via Unity Package Manager using a Git URL:

1. Open **Window > Package Manager**
2. Click the **+** button (top-left) > **Add package from git URL...**
3. Enter the following URL:

```
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

## License

[MIT](LICENSE.md)

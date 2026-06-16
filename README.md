# API2Cart Connector for nopCommerce

API Connector plugin that exposes REST API endpoints for store data access and
establishes a secure connection between a nopCommerce store and
[API2Cart](https://api2cart.com/) — a unified API to integrate with 40+ shopping
carts and marketplaces.

## Contents

| Path | Description |
|---|---|
| `Api2CartConnectorPlugin.cs` | Plugin entry point (install / uninstall, settings bootstrap). |
| `Controllers/`, `Services/`, `Filters/`, `Helpers/` | API endpoints, encryption, webhook dispatch. |
| `Helpers/ConnectorConfig.cs` | Reads the shipped `connector.config.json` (URL slug, friendly name). |
| `connector.config.json` | Shipped RSA encryption public key + key id and plugin metadata. |
| `Api2Cart.Connector.zip` | Built plugin package (single-folder layout). |
| `Api2Cart.Connector.marketplace.zip` | Ready-to-deploy package with `uploadedItems.json` for **Configuration → Local plugins → Upload plugin or theme**. |

## Supported versions

nopCommerce 4.90.

## Build

```
dotnet build Api2Cart.Connector.csproj -c Release
```

Reference assemblies (`Nop.Core`, `Nop.Data`, `Nop.Services`, `Nop.Web.Framework`,
`Autofac`, `FluentMigrator`) are expected in a sibling `../refs` directory. The two
zip packages in this repository are produced from this exact source.

## Support

Documentation: https://api2cart.com/docs/ — Contact: support@api2cart.com

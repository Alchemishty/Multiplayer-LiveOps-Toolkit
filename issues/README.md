# Issue status

The ten reports created on 2026-10-15 described code that has since changed. They were consolidated after checking the current source on 2026-10-23:

| Reports | Current implementation |
|---|---|
| 01–02: interface mismatch and unmatched directive | `IAccessoryVisualSync.Equip` returns `GameObject`; `ShopAPI` compiles. |
| 03, 08: refresh retry result and disposal | `ApiResponse` carries the final result; retry requests use `using`. |
| 04–05: shared accessory pools/slots | `AccessoryPool` is per-avatar, with instance dictionaries. |
| 06: API singleton lifetime | `ShopAPI` and `WalletAPI` use `DontDestroyOnLoad`. |
| 07: unchecked login reward response | The old panel was replaced; `DailyLoginAPI` checks `ApiResponse.IsSuccess`. |
| 09: duplicate currency conversion | `WalletAPI.ConvertCurrency` delegates to `ShopAPI`. |
| 10: form request used for JSON equipment writes | Equipment writes construct a JSON POST explicitly. |

These are source checks of the original reports, not a full correctness audit. Record new reproducible issues here rather than treating the old reports as an active backlog.

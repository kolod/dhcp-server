# Contract: window changes, confirmation dialog, localization keys

Extends `specs/001-dhcp-lease-server/contracts/ui-contract.md`. Auto-sized grids, no fixed text
widths; all text from string keys; automation names from the same keys.

## Main window — new "Address" group under the interface row

```text
Interface: [ComboBox]                                  [Start/Stop]
[x] Set a fixed address for this session
    Address:  [192 . 168 . 137 . 1 ]   Mask: [255.255.255.0]
    Gateway:  [                    ]   (optional)
    DNS 1:    [                    ]   DNS 2: [                ]   (optional)
    ⚠ <validation message for the first invalid field>
status / detail / notices ...
```

- The group is shown for every selected adapter. The checkbox is **on and locked** when the adapter
  cannot serve as it is (no address, link-local, mask too small, overlap) and **off** when it can;
  with the checkbox off the fields show the adapter's current values read-only.
- Fields are prefilled from the suggestion (research D9) when the checkbox is on and untouched, and
  never overwritten once the user typed; changing the adapter re-suggests.
- Each invalid field gets an inline message (`Field_<Kind>`); Start is disabled while any field is
  invalid or the adapter is down (`Reason_Down` as hint).
- While running/starting/stopping the group is disabled. During `Stopping` the status shows
  `Status_Restoring`.
- Selector: adapters that cannot serve as they are but are Ethernet/Wi-Fi are now **selectable** and
  shown with a "needs an address" marker (`Interface_NeedsAddress`) instead of greyed out; other
  unsupported types stay greyed with their reason.

## Confirmation dialog (before any change)

Title `Confirm_Title`; text `Confirm_Intro` with the adapter name. A table with columns
`Confirm_Setting | Confirm_Current | Confirm_New`, rows `Mode`, `Address`, `Mask`, `Gateway`, `DNS`;
changed rows in bold. A line `Confirm_RestoreNote` explains that the old settings come back when the
server stops. Buttons `Confirm_Apply` (default) and `Button_Cancel`. Cancel ⇒ nothing changes and the
server does not start. The dialog is a window with its own view model (no code-behind logic).

## Recovery banner (startup, restore pending after crash)

`Recovery_Pending` with the adapter name and previous settings; buttons `Recovery_Retry`,
`Recovery_Discard` (asks `Recovery_DiscardConfirm`). Start disabled while shown. Corrupt record:
`Recovery_Corrupt` with *Discard* only.

## New string keys (each in every `Strings*.resx` with a real translation)

Group: `Address_UseFixed`, `Address_Address`, `Address_Mask`, `Address_Gateway`, `Address_Dns1`,
`Address_Dns2`, `Address_Optional`, `Interface_NeedsAddress`, `Status_Stopping`, `Status_Restoring`.

Validation (`Field_<Kind>`): `Field_Required`, `Field_Malformed`, `Field_NotUnicast`,
`Field_LinkLocal`, `Field_MaskInvalid`, `Field_MaskSize`, `Field_NetworkOrBroadcast`,
`Field_GatewayOutsideSubnet`, `Field_DnsDuplicate`, `Field_SubnetOverlap`.

Dialog: `Confirm_Title`, `Confirm_Intro`, `Confirm_Setting`, `Confirm_Current`, `Confirm_New`,
`Confirm_Apply`, `Confirm_RestoreNote`, `Confirm_Mode`, `Confirm_Automatic`, `Confirm_Static`,
`Confirm_None`.

Recovery: `Recovery_Pending`, `Recovery_Retry`, `Recovery_Discard`, `Recovery_DiscardConfirm`,
`Recovery_Corrupt`.

Reasons/notices: `Reason_AddressApplyFailed` ({0} = step), `Reason_AddressRestoreFailed` ({0} = step,
{1} = previous settings), `Reason_SettingsLocked`, `Reason_RecordNotWritable`,
`Reason_AddressNotReady`, `Notice_SettingsApplied`, `Notice_SettingsRestored`,
`Notice_SettingsLeftUnchanged`, `Notice_AdapterMissing`.

Steps (`Step_<name>`): `Step_Capture`, `Step_WriteRecord`, `Step_SetAddress`, `Step_SetDns`,
`Step_ClearDns`, `Step_AddAddress`, `Step_SetAddressDhcp`, `Step_SetDnsDhcp`, `Step_Verify`,
`Step_WaitForAddress`, `Step_DeleteRecord`.

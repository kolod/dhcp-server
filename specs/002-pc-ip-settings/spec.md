# Feature Specification: PC Address Settings

**Feature Branch**: `002-pc-ip-settings`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Налаштування IP-адреси самого ПК. Зараз програма вимагає, щоб інтерфейс уже мав статичну адресу. Зміна адреси, маски, шлюзу і DNS, показ змін перед застосуванням і відновлення старих налаштувань при зупинці й після збою (принцип II)." (Configuring the PC's own IP address: today the program requires the interface to already have a fixed address. Change address, mask, gateway and DNS, show the changes before applying them, and restore the old settings on stop and after a crash.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Give the PC a fixed address for the session (Priority: P1)

A developer connects an IoT device to a laptop whose cable adapter has no usable address (automatic/unassigned or an address from some other network). Instead of opening the Windows network dialogs, they choose the adapter, keep or edit a suggested address, mask, optional gateway and optional DNS servers, review exactly what will change, confirm, and start the server. The adapter now has the fixed address and the DHCP server serves that subnet.

**Why this priority**: Today the user must configure the PC by hand before anything works; this removes the biggest setup hurdle and is the core of the feature.

**Independent Test**: With an adapter set to obtain an address automatically (and nothing connected), choose it, accept the suggested settings, confirm the shown changes, start the server, and verify the adapter now has the chosen address, mask, gateway and DNS and that the server is running on it.

**Acceptance Scenarios**:

1. **Given** the chosen adapter has no usable IPv4 address, **When** the user opens the address settings, **Then** a suggested address and mask are pre-filled that do not overlap any other active network connection.
2. **Given** the user entered valid settings, **When** they press Start, **Then** a confirmation dialog lists the adapter, its current settings and the new settings side by side, and nothing is changed until the user confirms.
3. **Given** the user confirms, **When** the settings are applied successfully, **Then** the server starts on the adapter with the new address.
4. **Given** the user cancels the confirmation, **When** the dialog closes, **Then** the adapter's settings are unchanged and the server is not started.
5. **Given** the adapter already has a usable fixed address, **When** the user prefers to keep it, **Then** they can start the server without changing anything and no confirmation of changes is shown.
6. **Given** the user enters invalid settings (malformed address, unsupported mask, gateway outside the subnet, address equal to the network or broadcast address, an address that overlaps another active connection), **When** they try to continue, **Then** the problem is shown next to the field and Start is not possible.

---

### User Story 2 - Get the old settings back (Priority: P1)

After the session the laptop must be exactly as it was. When the user stops the server or closes the program, the adapter returns to its previous configuration — including "obtain automatically" if that was the previous mode. If the program crashed or the PC lost power while settings were changed, the next start of the program first offers to put the old settings back.

**Why this priority**: A laptop that loses its normal network settings after a diagnostic session is worse than having no tool (constitution, Principle II). Changing settings without a reliable way back must not ship.

**Independent Test**: Record an adapter's settings (static and automatic cases), run a session that changes them, stop, and compare: they must be identical. Then run a session, kill the program abruptly, start it again, and verify the old settings are restored before the user can start another session.

**Acceptance Scenarios**:

1. **Given** the program changed the adapter's settings, **When** the user presses Stop, **Then** the previous address, mask, gateway and DNS (or automatic mode) are restored and the user is told when this has finished.
2. **Given** the program changed the adapter's settings, **When** the user closes the program, **Then** the settings are restored before the program exits.
3. **Given** the adapter was in automatic (DHCP) mode before, **When** settings are restored, **Then** the adapter is back in automatic mode, not given a fixed address.
4. **Given** the program ended abnormally while settings were changed, **When** it is started the next time, **Then** it detects the unfinished session, tells the user, restores the old settings, and only then allows a new session.
5. **Given** the adapter's settings were changed by someone else after the program applied them, **When** the program would restore, **Then** it does not overwrite the newer settings and tells the user that it left them as they are.
6. **Given** the adapter is gone (unplugged USB adapter) at restore time, **When** restoring, **Then** the user is told that it could not be restored now, and the program offers to retry at the next start.

---

### User Story 3 - Understand failures and never end up half-configured (Priority: P2)

Applying or restoring settings can fail (adapter disappeared, access denied, another tool is changing the adapter). The user sees which exact step failed and what state the adapter is in, and can retry. A failed apply never leaves the server running with a half-applied configuration, and never touches any adapter other than the chosen one.

**Why this priority**: Failure handling is mandated by Principle II, but the happy paths of stories 1 and 2 deliver value first.

**Independent Test**: Simulate a failure at each step (address, mask, gateway, DNS) of an apply and of a restore, and verify the message names the step, the adapter ends in a known state (fully old or reported otherwise), the server is not running after a failed apply, and other adapters are untouched.

**Acceptance Scenarios**:

1. **Given** applying fails midway, **When** the failure is detected, **Then** the program tries to put back the previous settings, names the failing step, and does not start the server.
2. **Given** restoring fails, **When** the failure is detected, **Then** the user sees the exact failing step and the settings that should be restored, and the "unfinished session" record is kept so a retry is offered later.
3. **Given** several adapters exist, **When** settings are applied or restored, **Then** no other adapter's settings change.

---

### Edge Cases

- The adapter has several IPv4 addresses: all of them are recorded and restored; during the session the adapter has only the chosen one.
- The adapter is disconnected (no cable) when the user wants to configure it: settings can still be prepared, but Start is refused with the existing "interface down" message without changing anything until the link is up.
- The program is started twice: the second instance must not apply or restore anything concurrently.
- The user changes the interface selection while an unfinished-session offer is pending: the offer still concerns the adapter that was changed, not the newly selected one.
- The suggested address is already in use by another device on the segment: the suggestion avoids it when it can be detected, and the user can change it.
- DNS servers are left empty: the adapter then has no DNS servers during the session and the previous ones come back afterwards.
- Gateway left empty: the adapter has no default route through that adapter during the session.
- The PC goes to sleep or the user logs off during a session: on resume or next start the same restore rules apply.
- The record of the old settings cannot be written (read-only folder): the user is warned before changing anything and can cancel.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The user MUST be able to enter, for the chosen adapter, an IPv4 address and mask, and optionally a default gateway and up to two DNS servers.
- **FR-002**: The system MUST pre-fill a suggested address and mask that do not overlap any other active network connection and MUST allow the user to change them.
- **FR-003**: The system MUST validate the entered settings (well-formed addresses, supported mask size, gateway inside the subnet, address not equal to the network or broadcast address, not link-local or loopback, not overlapping another active connection) and MUST block Start with a clear message next to the offending field when they are invalid.
- **FR-004**: Before changing anything the system MUST show a confirmation listing, for the chosen adapter, the current settings and the new settings, and MUST change nothing until the user explicitly confirms.
- **FR-005**: The system MUST NOT show the confirmation or change anything when the user keeps the adapter's existing usable fixed address.
- **FR-006**: Before the first change, the system MUST capture the adapter's complete previous IPv4 configuration (all addresses and masks, gateway, DNS servers, and whether it was automatic) and MUST persist it so it survives a crash.
- **FR-007**: The system MUST restore the captured configuration when the server is stopped, when the program is closed, and — as soon as the program starts — for any session that ended abnormally.
- **FR-008**: The system MUST restore "automatic" mode, not a fixed address, when the adapter was automatic before.
- **FR-009**: The system MUST NOT overwrite settings that differ from those it applied (changed by the user or another tool in the meantime) and MUST tell the user it left them unchanged.
- **FR-010**: The system MUST report a failure of applying or restoring with the exact failing step and the resulting state of the adapter.
- **FR-011**: After a failed apply the system MUST attempt to put the previous settings back and MUST NOT start the server.
- **FR-012**: The system MUST change only the chosen adapter's settings and MUST NOT change any other adapter, firewall rule, service, driver or system-wide setting.
- **FR-013**: The record of the old configuration MUST be kept until restoring has succeeded (or the user knowingly discards it) and MUST be removed afterwards.
- **FR-014**: The system MUST refuse to change anything when it cannot record the old configuration, and MUST tell the user why.
- **FR-015**: Only one running instance MAY change or restore adapter settings at a time.
- **FR-016**: The user MUST be told when applying and restoring have finished successfully.
- **FR-017**: All user-visible text of this feature MUST be available in English, Ukrainian, German, French, Spanish and Japanese.

### Key Entities

- **Address Settings**: What the user wants for the adapter during a session: address, mask, optional gateway, optional DNS servers.
- **Saved Configuration**: The adapter's complete previous IPv4 configuration (every address and mask, gateway, DNS servers, automatic or fixed mode) plus which adapter it belongs to and the settings applied for the session; kept until restore succeeds.
- **Change Preview**: The side-by-side comparison of current and new settings shown for confirmation.
- **Unfinished Session**: A saved configuration found at program start that was never restored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with an adapter that has no usable address can go from choosing it to a running server in under 1 minute without opening any Windows network dialog.
- **SC-002**: In 100% of tested sessions, nothing about the adapter changes before the user confirms the shown preview.
- **SC-003**: After stopping or closing, the adapter's settings equal the original ones in 100% of tested cases (fixed and automatic, one and several addresses, with and without gateway and DNS).
- **SC-004**: After an abnormal termination, in 100% of tested cases the old settings are restored before a new session can be started.
- **SC-005**: In 100% of tested failure cases the message names the failing step and the server is not running after a failed apply.
- **SC-006**: In all tests, no adapter other than the chosen one changes.
- **SC-007**: Applying or restoring settings takes under 10 seconds on a typical PC.

## Assumptions

- Only IPv4 settings of the chosen adapter are handled; IPv6 settings are left untouched.
- The suggested default is a private /24 network (for example 192.168.137.1) chosen so that it does not overlap other active connections.
- Applying settings is part of Start and restoring is part of Stop/exit; there is no separate "apply" button outside a session.
- The user has administrator rights (the program already runs elevated).
- The saved configuration is stored next to the program (portable-app rule); if that location is not writable, no change is made.
- Internet sharing, probing services on devices and quick launch are separate features and not part of this one.
- Settings the user changed manually during a session win over the automatic restore (see FR-009).

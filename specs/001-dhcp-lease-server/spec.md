# Feature Specification: DHCP Lease Server

**Feature Branch**: `001-dhcp-lease-server`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "DHCP-сервер на обраному інтерфейсі з видачею адрес IoT-пристроям і списком оренд" (a DHCP server on a chosen interface that gives addresses to IoT devices and shows a list of leases)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Start serving addresses on a chosen interface (Priority: P1)

A developer connects an IoT device to their laptop with a network cable (or an isolated switch). They open the application, pick the network interface the device is plugged into, and start the server. The device asks for an address and receives one, so the developer can reach it immediately without configuring anything on the device.

**Why this priority**: This is the core purpose of the tool. Without a running server that hands out addresses on exactly one chosen interface, nothing else has value.

**Independent Test**: Connect a DHCP client (a real device or a test client) to the chosen interface, start the server, and verify the client receives an address, mask and gateway/server information that lets it communicate with the laptop.

**Acceptance Scenarios**:

1. **Given** the application is open and at least one eligible interface exists, **When** the user selects an interface and starts the server, **Then** the server begins answering address requests on that interface only and shows a "running" state.
2. **Given** the server is running, **When** an IoT device broadcasts an address request on the chosen interface, **Then** it receives an address from the pool of that interface's subnet and can communicate with the laptop.
3. **Given** the server is running, **When** the same device restarts and asks again, **Then** it receives the same address as before.
4. **Given** the server is running, **When** a device asks for an address on a different interface of the laptop, **Then** the server does not answer.
5. **Given** the user has not chosen an interface, **When** the user tries to start the server, **Then** the server does not start and the user is told that an interface must be chosen.
6. **Given** the server is running, **When** the user stops it (or closes the application), **Then** the server stops answering immediately and no listener remains.

---

### User Story 2 - See the list of leases (Priority: P1)

While the server is running, the developer wants to know which devices got which addresses. The application shows a live list of leases: for each device its address, hardware (MAC) address, host name if the device reported one, and when the lease started and expires. New devices appear in the list without any manual refresh.

**Why this priority**: Finding out "what address did my device get?" is the main reason a developer uses this tool; the list is as essential as the server itself.

**Independent Test**: Start the server, connect one or more devices (or test clients), and verify each appears in the list with correct address and MAC, and that the entry updates when the device renews or releases its lease.

**Acceptance Scenarios**:

1. **Given** the server is running, **When** a device receives an address, **Then** a new entry for that device appears in the lease list within one second.
2. **Given** a device is in the list, **When** it renews its lease, **Then** its entry shows the updated expiry time and no duplicate entry is created.
3. **Given** a device is in the list, **When** it releases its address or its lease expires, **Then** the entry is marked as no longer active (and is not offered as in use).
4. **Given** the list contains entries, **When** the user copies an address from the list, **Then** the address is available in the clipboard for use in other tools.
5. **Given** the server is stopped, **When** the user looks at the list, **Then** the leases from the last session remain visible and are marked as inactive.

---

### User Story 3 - Refuse unsafe situations (Priority: P1)

The developer sometimes picks the wrong interface — for example the one connected to the office network, which already has a DHCP server. The application must never become a rogue DHCP server: it checks the chosen interface before serving and refuses to start, with a clear explanation, when the situation is unsafe.

**Why this priority**: A second DHCP server on a shared network breaks every host on it. Safety is non-negotiable and must ship together with the ability to serve at all.

**Independent Test**: Try to start the server on (a) an interface where another DHCP server answers, (b) a disconnected interface, (c) an interface whose subnet overlaps another active interface, and verify it refuses each time with an explanation and answers no requests.

**Acceptance Scenarios**:

1. **Given** another DHCP server is present on the chosen interface, **When** the user starts the server, **Then** it refuses to start and tells the user that another DHCP server was detected.
2. **Given** the chosen interface is down or disconnected, **When** the user starts the server, **Then** it refuses to start and explains why.
3. **Given** the chosen interface's subnet overlaps with another active interface, **When** the user starts the server, **Then** it refuses to start and explains the conflict.
4. **Given** the server is running, **When** the chosen interface goes down or disappears, **Then** the server stops answering immediately and the user is notified.
5. **Given** an address in the pool already answers on the network without being leased, **When** a device asks for an address, **Then** that address is not offered.

---

### User Story 4 - Choose the interface conveniently (Priority: P2)

The developer sees a list of the laptop's network interfaces with a friendly name, current IPv4 address and connection status, and picks one. Interfaces that cannot be used are shown as unavailable with the reason, so the developer understands why they cannot be selected.

**Why this priority**: Picking the correct interface is the key safety step; a clear list lowers the risk of choosing wrongly, but the server could still work with a simpler selector.

**Independent Test**: Open the application with several interfaces present (wired, Wi-Fi, virtual, disconnected) and verify each is listed with name, address and status, and that unusable ones cannot be selected.

**Acceptance Scenarios**:

1. **Given** the laptop has several interfaces, **When** the application opens, **Then** all interfaces are listed with name, IPv4 address and status.
2. **Given** an interface is disconnected or has no IPv4 address, **When** the user looks at the list, **Then** it is shown as unavailable together with the reason.
3. **Given** the user previously chose an interface, **When** the application is opened again, **Then** the same interface is preselected if it still exists, but the server is not started automatically.

---

### Edge Cases

- The pool is exhausted (more devices than free addresses): further devices get no address, the user is informed, and existing leases are not disturbed.
- The chosen interface has no IPv4 address or a link-local address only: the server refuses to start and explains that the interface needs an address first.
- A device asks for a specific address it used before and that address is still free: it receives that address; if it is taken, it receives a different one.
- Two devices report the same host name: both are listed, distinguished by their MAC addresses.
- A device sends malformed or unrelated network traffic to the server port: it is ignored and the server keeps running.
- The application is closed unexpectedly: on the next start no server is running until the user starts it explicitly, and previous leases are still shown as inactive.
- The laptop's clock changes (time zone, daylight saving, manual adjustment): active leases are not lost or expired incorrectly.
- Another program on the laptop already uses the DHCP server port: the server refuses to start and explains the conflict.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let the user choose exactly one network interface and MUST NOT start serving until one has been chosen.
- **FR-002**: The system MUST answer address requests only on the chosen interface and MUST ignore requests arriving on any other interface.
- **FR-003**: The system MUST list the laptop's interfaces with name, IPv4 address and connection status, and MUST mark unusable interfaces (down, no IPv4 address, not Ethernet/Wi-Fi-like, subnet overlapping another active interface) as unavailable with a reason.
- **FR-004**: Before serving, the system MUST check the chosen interface for an existing DHCP server and MUST refuse to start, with an explanation, if one answers.
- **FR-005**: The system MUST issue addresses only from a pool inside the subnet of the chosen interface, and MUST NEVER issue the laptop's own address or the gateway address.
- **FR-006**: The system MUST NOT offer an address that already answers on the network without being leased by this server.
- **FR-007**: The system MUST give a returning device (identified by its hardware address) the same address while its lease is valid or while the address remains free.
- **FR-008**: The system MUST support the full address-request exchange of a standard IPv4 DHCP client: discovery, offer, request, acknowledgement, renewal, release, and refusal of an unsuitable request.
- **FR-009**: Each lease MUST have a finite duration, and the system MUST make an expired or released address available for reuse.
- **FR-010**: The system MUST show a list of leases with, for each device: IP address, hardware address, host name (when reported), lease start, lease expiry and whether the lease is active.
- **FR-011**: The lease list MUST update automatically while the server runs, without manual refresh.
- **FR-012**: The system MUST let the user copy an address or hardware address from the list.
- **FR-013**: The system MUST keep leases across stopping and restarting the server and across application restarts, showing leases from earlier sessions as inactive until they are renewed or still valid.
- **FR-014**: The system MUST stop answering immediately when the user stops the server, changes the interface, the interface goes down, or the application exits, and MUST NOT leave any listener running.
- **FR-015**: The system MUST show the current server state (stopped, starting, running, failed) and the reason for any refusal or failure in the user's language.
- **FR-016**: The system MUST report to the user when the pool is exhausted, and MUST NOT disturb existing leases in that case.
- **FR-017**: The system MUST ignore malformed or unrelated network traffic without crashing or stopping.
- **FR-018**: The system MUST never start serving automatically when the application is opened; the user MUST start it explicitly.
- **FR-019**: All user-visible text MUST be available in English, Ukrainian, German, French, Spanish and Japanese.

### Key Entities

- **Network Interface**: A network adapter of the laptop; has a friendly name, connection status, IPv4 address and mask, and a usability state with a reason if unusable.
- **Address Pool**: The range of addresses that may be handed out on the chosen interface; derived from the interface's subnet and excluding the laptop's own address, the gateway address and the network/broadcast addresses.
- **Lease**: An assignment of an address to a device for a limited time; has IP address, hardware address, optional host name, start time, expiry time and state (active, expired, released).
- **Server Session**: A period during which the server is running on one chosen interface; has a state and a reason when it ends abnormally.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who has the application open can go from selecting an interface to a connected device having an address in under 30 seconds.
- **SC-002**: A device that asks for an address on the chosen interface receives one in under 2 seconds in at least 99% of attempts.
- **SC-003**: A new or changed lease appears in the list in under 1 second after the device receives its address.
- **SC-004**: In 100% of tested unsafe situations (another DHCP server present, interface down, overlapping subnet, wrong interface), the server refuses to start or stops and no address is issued.
- **SC-005**: No address request on any non-chosen interface is ever answered during testing.
- **SC-006**: At least 30 devices can hold leases simultaneously with all of them correctly listed and no address given to two devices at once.
- **SC-007**: After stopping the server or closing the application, no device can obtain an address from the laptop.
- **SC-008**: A first-time user can find the address of their device from the lease list without reading documentation.

## Assumptions

- The user is a developer or diagnostician connecting a small number of IoT devices (up to a few dozen) over a direct cable or an isolated switch; large production networks are out of scope.
- Only IPv4 is served; one interface and one address pool at a time (per the project constitution).
- The chosen interface already has a static IPv4 address; changing the laptop's own address, and Internet sharing, are separate features and are not part of this one.
- The address pool is derived automatically from the interface's subnet by default; letting the user customize pool boundaries and lease duration may follow later, and this feature uses sensible defaults (a lease of a few hours).
- Lease data is stored next to the application, in line with the portable-application rule, and failure to store it is not fatal (leases then only live for the session).
- The user has administrator rights, since listening for address requests requires elevation.
- Probing common services on leased devices and quick launch of external programs are separate features.

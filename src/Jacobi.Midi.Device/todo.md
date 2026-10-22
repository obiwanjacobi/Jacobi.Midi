# Device - To Do

Known gaps and loose ends of the address map and the message exchange (see [Addressing/readme.md](Addressing/readme.md) and [Exchange/readme.md](Exchange/readme.md)).

## Message exchange

- [ ] **Message exchange patterns** beyond the built-in `oneWay` and `acknowledged` protocols (`ExchangeProtocols`), as state machines (`ITransferMachine`) and later configurable.
  - [ ] Roland handshake protocol: WSD/RQD → ACK → DAT → ACK … EOD, ERR → resend, RJC → abort, with retries and timeouts.
  - [ ] Framed transactions: Yamaha Bulk Header → blocks → Bulk Footer (stored Live Set Sounds of the CP88, CK88, YC).
  - [ ] Commands without data: write/store requests (Korg Program Write Request, Yamaha Store To Flash) with their completed/error replies.
  - [ ] Device modes: requests that are only accepted in some modes (Korg i5M, DS-8 panel modes), with mode request/change messages.
  - [ ] A hook for a manual operation on the device (e.g. "press the bulk dump key"): the exchange asks the client to prompt the user and waits for the data or a cancel.

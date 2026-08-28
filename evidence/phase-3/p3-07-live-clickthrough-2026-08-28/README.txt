P3-07 - supplementary live click-through, 2026-08-28

Purpose: confirm the redeployed MerchandisingApi Windows Service is serving
current endpoints and that the Procurement client still works end to end
against it, after P3-08's changes. This is a confidence check, NOT new
acceptance evidence for P3-07's own Done-when boxes - the card stays open
(tasks.md) because its one remaining box is unrelated to functionality:

  [ ] Keyboard navigation and focus order work at 1366x768 and 125% scaling
      - not exercised here; this workstation still runs 1920x1200 @ 100%.

Pre-check:
  Get-Service MerchandisingApi -> Running, StartType Automatic
  curl -sk https://MERCH-HOST:8443/api/v1/suppliers -> HTTP 401 (not 404):
  the route exists and requires auth, confirming the current build (with
  P3-08's documentation-only changes, no API behaviour change) is live.

Method: Merchandising.Procurement.exe launched and driven via .NET UI
Automation (System.Windows.Automation from PowerShell) - ValuePattern to
fill text fields, SelectionItemPattern to pick DataGrid rows and tabs,
InvokePattern to click buttons - screen-captured with System.Drawing after
each step. Same technique p3-07-procurement-client.txt section 4 recorded
originally (there: SetCursorPos/mouse_event/SendKeys; here: UI Automation
patterns directly against the live HWND, same principle - driving the real
window, not a design-time preview).

Signed in as procurementofficer (password from
C:\ProgramData\MerchandisingSystem\config\installation-credentials.txt,
never written to any file in this evidence folder or the repository).

Screenshots, in order:
  01-launched.png                      window opens, sign-in panel shown
  02-signed-in.png                     signed in; Connected; session banner
  03-suppliers.png                     Suppliers tab, blank-query search,
                                        729 supplier(s) found
  04-new-order-supplier-selected.png   New Order tab, "Cebu" search ->
                                        Cebu Tools & Fasteners Corp selected
  05-new-order-line-added.png          "hammer" search -> HDW-1001 selected,
                                        qty 10, cost 220.0000, line added
  06-new-order-created.png             Purchase order PO-20260828-0001
                                        created (Draft); form reset for the
                                        next entry (correct client behaviour)
  07-orders-selected.png               Orders tab, PO-20260828-0001 selected,
                                        one line, Draft
  08-orders-submitted.png              Submit clicked -> "... is now
                                        Submitted."; detail grid updated
  09-history.png                       History tab, blank filters ->
                                        PO-20260828-0001 shows Ordered Qty
                                        10.000, Ordered Value 2200.0000
                                        (= 10 x 220.0000, server-computed
                                        per P3-06), Outstanding 10.000

Every status line, order number, and correlation ID above came from the
live server response, not the client. No regression found; nothing here
changes P3-07's or P3-08's status in tasks.md.

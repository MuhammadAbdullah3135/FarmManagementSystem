# FMS User Guide

## Getting Started
1. Register an account at /register
2. Create your first farm
3. Configure animal types, breeds, sex options, and statuses
4. Add locations for your farm

## Animal Management
- Register animals with tag numbers, types, breeds, and status
- Track weight over time with historical records
- Transfer animals between locations
- View animal timeline with all events

### Scanning a tag, and printing labels

- **Scanning** — the code button in the header opens the camera, and the Animals page has the
  same action labelled *Scan animal tag*. Point it at one of your QR labels and the animal's
  record opens.
- **Scanning works offline**, for the animals your device already holds: the scan is resolved
  against the list the device has stored, without asking the server. Offline, an animal that is
  not stored yet is reported as such — connect once and it will be found next time.
- **Printing labels** — *Animals → QR labels* builds a sheet for the page you are looking at,
  including your search and page size. Every label carries the tag number and the name as text
  beside the code, so it can be read without a scanner. It needs a connection (the codes are
  built by the server from this deployment's own address) and it is a desk job: an Android
  WebView cannot open a print dialog, so print from a browser.
- **Renumbering does not invalidate labels.** The code carries the animal's internal identity,
  not its tag number, so changing an animal's tag leaves the labels already stuck on it working —
  only the number printed on the paper becomes out of date.
- **Codes that do not open anything** say which case they are: a code that is not one of ours
  (*that code is not an animal tag*), a label for an animal this farm does not have (*no animal
  in this farm carries that tag*), and a label that is not on this device while you are offline —
  so it is clear whether to check the animal or the connection.
- **Another farm's label resolves to nothing here, by design.** Codes carry no farm, and a scan
  is always resolved against the farm you have active, so a neighbour's tag can never open a
  record in yours. Switch to that farm and scan again.

## Daily Operations
- Log feeding records by feed type and quantity
- Create and manage diet plans
- Set up feeding schedules
- Assign and track farm tasks

## Finance
- Record expenses with categories and payment methods
- Track income from various sources
- View financial reports and summaries

## Health Management
- Record medical treatments with diagnosis and vet info
- Manage medicine stock and usage
- Schedule and track vaccinations
- Monitor veterinary costs

## Breeding
- Record breeding pairs and methods
- Track gestation progress
- Record births with offspring details
- View lineage and pedigree

## Inventory
- Manage inventory items with stock levels
- Record stock movements (purchases, usage, adjustments)
- Track suppliers and customers

## Reports
- Animal reports with statistics
- Financial summaries and trends
- Feed consumption analytics
- Breeding and health reports

## Admin
- View audit trail of all changes (Admin/Manager/Accountant only)

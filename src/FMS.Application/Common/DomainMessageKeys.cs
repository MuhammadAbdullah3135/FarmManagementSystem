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


    // ── supersede ─────────────────────────────────────────────────────────────────────────
    // One constant per sentence, shared across the services that answer with it:
    // “This completion was already applied” is said by 1 service files, and each one
    // gets the same sentence in the reader's language rather than its own English.

    /// <summary>The exact English this API has always answered: “This completion was already applied”</summary>
    public const string CompletionAlreadyApplied = "validation.supersede.completionAlreadyApplied";

    /// <summary>The exact English this API has always answered: “Task is already completed”</summary>
    public const string TaskAlreadyCompleted = "validation.supersede.taskAlreadyCompleted";

}

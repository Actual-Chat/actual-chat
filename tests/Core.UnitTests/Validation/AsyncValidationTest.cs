using System.ComponentModel.DataAnnotations;

namespace ActualChat.Core.UnitTests.Validation;

// Pins the DataAnnotations contract EditContextAsyncValidator relies on: the two
// Validator.TryValidate*Async entry points must run our sync attributes and, once
// those pass, any AsyncValidationAttribute on the same property.
// The expected errors are ValidationKeys entries rather than sentences: no IStringLocalizer
// is registered here, so each attribute falls back to the key it would have localized.
public class AsyncValidationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("", null)]
    [InlineData("+1 (555) 555-5550", null)]
    [InlineData("12345", ValidationKeys.PhoneTooShort)]
    [InlineData("+1 555 555 555 555 555", ValidationKeys.PhoneTooLong)]
    [InlineData("+1 555 555 55x0", ValidationKeys.PhoneInvalidCharacters)]
    public async Task PhoneIsValidated(string phone, string? expectedError)
    {
        // arrange
        var model = new PhoneModel { Phone = phone };

        // act
        var errors = await ValidateProperty(model, nameof(PhoneModel.Phone), phone);

        // assert
        errors.Should().Equal(expectedError is null ? [] : [expectedError]);
    }

    [Theory]
    [InlineData("someone@example.com", null)]
    [InlineData("foo@", ValidationKeys.EmailInvalid)]
    [InlineData("12345", ValidationKeys.PhoneTooShort)]
    [InlineData("abc", ValidationKeys.PhoneOrEmailRequired)]
    public async Task PhoneOrEmailIsValidated(string input, string? expectedError)
    {
        // arrange
        var model = new PhoneOrEmailModel { PhoneOrEmail = input };

        // act
        var errors = await ValidateProperty(model, nameof(PhoneOrEmailModel.PhoneOrEmail), input);

        // assert
        errors.Should().Equal(expectedError is null ? [] : [expectedError]);
    }

    [Fact]
    public async Task AsyncAttributeRunsWhenSyncValidationPasses()
    {
        // arrange
        var model = new AsyncModel { Value = "taken" };
        var validationContext = new ValidationContext(model);
        var results = new List<ValidationResult>();

        // act
        var isValid = await Validator.TryValidateObjectAsync(model, validationContext, results, true);

        // assert
        isValid.Should().BeFalse();
        results.Select(x => x.ErrorMessage).Should().Equal(["Value is already taken."]);
    }

    [Fact]
    public async Task AsyncAttributeIsSkippedWhenSyncValidationFails()
    {
        // arrange
        var model = new AsyncModel { Value = "" };
        var validationContext = new ValidationContext(model);
        var results = new List<ValidationResult>();

        // act
        var isValid = await Validator.TryValidateObjectAsync(model, validationContext, results, true);

        // assert
        isValid.Should().BeFalse();
        results.Select(x => x.ErrorMessage).Should().Equal(["The Value field is required."]);
        model.AsyncCallCount.Should().Be(0);
    }

    [Fact]
    public void AsyncAttributeIsSkippedByTheSyncValidator()
    {
        // arrange
        var model = new AsyncModel { Value = "taken" };
        var validationContext = new ValidationContext(model);
        var results = new List<ValidationResult>();

        // act
        var isValid = Validator.TryValidateObject(model, validationContext, results, true);

        // assert
        isValid.Should().BeTrue();
        model.AsyncCallCount.Should().Be(0);
    }

    // Private methods

    private static async Task<string[]> ValidateProperty(object model, string memberName, object? value)
    {
        var validationContext = new ValidationContext(model) { MemberName = memberName };
        var results = new List<ValidationResult>();
        await Validator.TryValidatePropertyAsync(value, validationContext, results);
        return results.Select(x => x.ErrorMessage!).ToArray();
    }

    // Nested types

    private sealed class PhoneModel
    {
        [PhoneNumber, Display(Name = "Phone")]
        public string Phone { get; set; } = "";
    }

    private sealed class PhoneOrEmailModel
    {
        [PhoneOrEmail, Display(Name = "Phone or email")]
        public string PhoneOrEmail { get; set; } = "";
    }

    private sealed class AsyncModel
    {
        public int AsyncCallCount;
        [Required, TakenValue, Display(Name = "Value")]
        public string Value { get; set; } = "";
    }

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class TakenValueAttribute : AsyncValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            => ValidationResult.Success;

        protected override Task<ValidationResult?> IsValidAsync(
            object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        {
            if (validationContext.ObjectInstance is AsyncModel model)
                Interlocked.Increment(ref model.AsyncCallCount);

            return Task.FromResult(value as string == "taken"
                ? validationContext.Error("Value is already taken.")
                : ValidationResult.Success);
        }
    }
}

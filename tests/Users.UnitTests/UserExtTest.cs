using ActualLab.Generators;

namespace ActualChat.Users.UnitTests;

public class UserExtTest
{
    [Theory]
    [InlineData("1-2187303414", "jjj.bbb@gmail.com", "tr/lL/pjB5kWvzY8dWhnQ45qCmAlrNGQE3Y6PrM75xk=", "KBHWN6oFNDXdTI6kkY7X7PaHM3AgJa+EoXUKV38bssU=")]
    [InlineData("91-6361751111", "aklqb6218bs1ekl@privaterelay.appleid.com", "nOycpTkso7wJPVX2oNlW/KvckJn1nZ0QQPb/j/ekAu4=", "gUg5zbI54zy/7wZCcdbw3rXH15gmfZhc+iCE6qI3J6g=")]
    public void ShouldCreateCorrectIdentities(string phone, string email, string expectedPhoneHash, string expectedEmailHash)
    {
        // act
        var user = new User("user1", "User 1").WithPhoneIdentities(new Phone(phone)).WithEmailIdentities(email);

        // assert
        user.Identities.Keys.Select(x => x.Id)
            .Should()
            .BeEquivalentTo(new Symbol[] {
                $"email/{email}",
                $"email-hash/{expectedEmailHash.OrdinalReplace("/", "\\/")}",
                $"phone/{phone}",
                $"phone-hash/{expectedPhoneHash.OrdinalReplace("/", "\\/")}",
            });
    }
}

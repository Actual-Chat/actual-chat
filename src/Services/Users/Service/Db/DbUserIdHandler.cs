using Stl.Conversion;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Generators;

namespace ActualChat.Users.Db
{
    public class DbUserIdHandler : DbUserIdHandler<string>
    {
        public DbUserIdHandler(IConverterProvider converters)
            : base(converters, null)
        {
            var rsg = new RandomStringGenerator(6 /* for now */, RandomStringGenerator.Base32Alphabet);
            Generator = () => rsg.Next();
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ActualChat.Db
{
    public class UlidToStringConverter : ValueConverter<Ulid, string>
    {
        private static readonly ConverterMappingHints DefaultHints = new(26, unicode:false);

        public UlidToStringConverter(ConverterMappingHints mappingHints = null!)
            : base(x => x.ToString(), x => Ulid.Parse(x), DefaultHints.With(mappingHints))
        { }
    }
}

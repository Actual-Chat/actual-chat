using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ActualChat.Db
{
    public class UlidToStringConverter : ValueConverter<Ulid, string>
    {
        private static readonly ConverterMappingHints DefaultHints = new ConverterMappingHints(size: 26, unicode:false);

        public UlidToStringConverter(ConverterMappingHints mappingHints = null!)
            : base(
                convertToProviderExpression: x => x.ToString(),
                convertFromProviderExpression: x => Ulid.Parse(x),
                mappingHints: DefaultHints.With(mappingHints))
        {
        }
    }
}
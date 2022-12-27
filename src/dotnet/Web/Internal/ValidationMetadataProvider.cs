using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ActualChat.Web.Internal;

public class ValidationMetadataProvider : IValidationMetadataProvider
{
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        var identity = context.Key;
        var containerType = identity.ContainerType;
        if (containerType is {IsGenericType: true}
            && containerType.GetGenericTypeDefinition()==typeof(Option<>)) {
            context.ValidationMetadata.PropertyValidationFilter = OptionPropsValidationFilter.Instance;
        }
    }
}

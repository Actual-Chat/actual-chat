export class NullableJSObjectReference
{
    public value: unknown;

    public static create(target: unknown) : NullableJSObjectReference
    {
        if (!target)
            return {
                value : null
            };

        // From .NET10 it should be possible to return a JSObjectReference or null directly.
        // So far, we do it manually.
        const jsObjectReference = DotNet.createJSObjectReference(target);
        return {
            value : jsObjectReference
        };
    }
}

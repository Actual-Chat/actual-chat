export class NullableJSObjectReference
{
    public value: null | any;

    public static create(target: any | null) : NullableJSObjectReference
    {
        if (!target)
            return {
                value : null
            };

        // From .NET10 it should be possible to return a JSObjectReference or null directly.
        // So far, we do it manually.
        // @ts-ignore
        const jsObjectReference = DotNet.createJSObjectReference(target);
        return {
            value : jsObjectReference
        };
    }
}

namespace ActualChat.Text;

public static class LoremIpsum
{
    private static readonly string[] Sentences = [
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
        "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore.",
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia.",
        "Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit.",
        "Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet.",
        "Ut enim ad minima veniam, quis nostrum exercitationem ullam corporis suscipit.",
        "Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse.",
        "At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis.",
        "Nam libero tempore, cum soluta nobis est eligendi optio cumque nihil impedit.",
        "Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe.",
        "Itaque earum rerum hic tenetur a sapiente delectus, ut aut reiciendis.",
        "Nulla pariatur excepteur sint occaecat cupidatat non proident deserunt mollit.",
        "Curabitur pretium tincidunt lacus sed porttitor lectus nibh vulputate.",
        "Fusce dapibus, tellus ac cursus commodo, tortor mauris condimentum nibh.",
        "Donec id elit non mi porta gravida at eget metus vestibulum.",
        "Praesent commodo cursus magna, vel scelerisque nisl consectetur et viverra.",
        "Maecenas sed diam eget risus varius blandit sit amet non magna.",
        "Cras mattis consectetur purus sit amet fermentum aenean lacinia bibendum.",
        "Integer posuere erat a ante venenatis dapibus posuere velit aliquet.",
        "Vivamus sagittis lacus vel augue laoreet rutrum faucibus dolor auctor.",
        "Morbi leo risus, porta ac consectetur ac, vestibulum at eros donec.",
        "Aenean eu leo quam pellentesque ornare sem lacinia quam venenatis.",
        "Nullam quis risus eget urna mollis ornare vel eu leo praesent.",
    ];

    public static string GetRandomSentence()
        => Sentences[Random.Shared.Next(Sentences.Length)];
}

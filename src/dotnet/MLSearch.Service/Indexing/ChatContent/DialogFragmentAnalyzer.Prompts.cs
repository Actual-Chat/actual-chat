namespace ActualChat.MLSearch.Indexing.ChatContent;

internal partial class DialogFragmentAnalyzer
{
    public static class Prompts
    {
        public const string IsDialogAboutTheSameTopicWithAnalysis =
            """
            You are tasked with analyzing a fragment of a chat conversation to determine whether it focuses on a single topic or multiple topics. This task is important for understanding conversation coherence and topic consistency in digital communications.

            Here is the chat fragment you need to analyze:

            <chat_fragment>
            {{CHAT_FRAGMENT}}
            </chat_fragment>

            To complete this task, follow these steps:

            1. Carefully read through the entire chat fragment.
            2. Identify the main topics or subjects discussed in the conversation.
            3. Pay attention to any shifts in the conversation or introduction of new topics.
            4. Consider the following guidelines to determine if the conversation is about a single topic:
               - The majority of messages relate to one central theme or subject.
               - Any deviations from the main topic are brief and the conversation quickly returns to the primary subject.
               - Subtopics or related ideas still connect to the main topic.
               - There are no significant or prolonged shifts to unrelated subjects.
               - Shifts to unrelated subjects in the end of the given fragment may indicate changing in  discussion topic and should be considered as multiple topics.

            5. Based on your analysis, make a decision on whether the chat fragment is about a single topic or multiple topics.

            6. Provide your final answer in the following format:
               <decision>
               [State whether the conversation is about the only single topic or not. Just answer with yes or no.]
               </decision>

               <justification>
               [Provide a brief explanation for your decision, referencing specific elements of the conversation that support your conclusion]
               </justification>

            7. In <reasoning> tags, provide your thought process as you analyze the conversation. Consider the following questions:
               - What is the main topic (if any) of the conversation?
               - Are there any deviations from this topic? If so, how significant are they?
               - Do the participants stay focused on a single subject, or do they drift between multiple unrelated topics?


            Remember to base your decision solely on the content of the provided chat fragment. Do not make assumptions about information not present in the conversation.
            """;

        public const string IsDialogAboutTheSameTopic =
            """
            You are tasked with analyzing a fragment of a chat conversation to determine whether it focuses on a single topic or multiple topics. This task is important for understanding conversation coherence and topic consistency in digital communications.

            Here is the chat fragment you need to analyze:

            <chat_fragment>
            {{CHAT_FRAGMENT}}
            </chat_fragment>

            To complete this task, follow these steps:

            1. Carefully read through the entire chat fragment.
            2. Identify the main topics or subjects discussed in the conversation.
            3. Pay attention to any shifts in the conversation or introduction of new topics.
            4. Consider the following guidelines to determine if the conversation is about a single topic:
               - The majority of messages relate to one central theme or subject.
               - Any deviations from the main topic are brief and the conversation quickly returns to the primary subject.
               - Subtopics or related ideas still connect to the main topic.
               - There are no significant or prolonged shifts to unrelated subjects.
               - Shifts to unrelated subjects in the end of the given fragment may indicate changing in  discussion topic and should be considered as multiple topics.

            5. Based on your analysis, make a decision on whether the chat fragment is about a single topic or multiple topics.

            6. Provide your final answer in the following format:
               <decision>
               [State whether the conversation is about the only single topic or not. Just answer with yes or no.]
               </decision>

            Remember to base your decision solely on the content of the provided chat fragment. Do not make assumptions about information not present in the conversation.
            """;

        public const string ChooseMoreProbableDialogWithAnalysis =
            """
            You are tasked with evaluating several dialog fragment examples and determining which one appears to be the most realistic. Your goal is to analyze the language, context, and flow of each fragment to make an informed decision.

            Here are the dialog fragments you will be evaluating:

            <dialog_fragments>
            {{DIALOG_FRAGMENTS}}
            </dialog_fragments>

            For each dialog fragment:
            1. Analyze the language used, including vocabulary, sentence structure, and tone.
            2. Consider the context and coherence of the conversation.
            3. Evaluate the naturalness of the flow and turn-taking between speakers.
            4. Look for subtle nuances that make the dialog feel authentic or artificial.

            After analyzing each fragment, compare them to one another, noting the strengths and weaknesses of each in terms of realism.

            To make your final decision:
            1. Identify the fragment that best captures natural human conversation.
            2. Consider factors such as appropriate use of colloquialisms, varied sentence lengths, and realistic reactions or responses.
            3. Think about which fragment would be most believable if overheard in a real-life setting.

            Provide your reasoning for your choice, explaining why you believe it to be the most realistic compared to the others. Include specific examples from the chosen fragment that support your decision.

            Present your analysis and final decision in the following format:

            <analysis>
            [Your detailed analysis of each fragment and comparison between them]
            </analysis>

            <decision>
            [Your choice of the most realistic dialog fragment. Answer with option name only.]
            </decision>

            <justification>
            [Your reasoning for choosing this fragment as the most realistic, with specific examples]
            </justification>
            """;

        public const string ChooseMoreProbableDialog =
            """
            You are tasked with evaluating several dialog fragment examples and determining which one appears to be the most realistic. Your goal is to analyze the language, context, and flow of each fragment to make an informed decision.

            Here are the dialog fragments you will be evaluating:

            <dialog_fragments>
            {{DIALOG_FRAGMENTS}}
            </dialog_fragments>

            For each dialog fragment:
            1. Analyze the language used, including vocabulary, sentence structure, and tone.
            2. Consider the context and coherence of the conversation.
            3. Evaluate the naturalness of the flow and turn-taking between speakers.
            4. Look for subtle nuances that make the dialog feel authentic or artificial.

            After analyzing each fragment, compare them to one another, noting the strengths and weaknesses of each in terms of realism.

            To make your final decision:
            1. Identify the fragment that best captures natural human conversation.
            2. Consider factors such as appropriate use of colloquialisms, varied sentence lengths, and realistic reactions or responses.
            3. Think about which fragment would be most believable if overheard in a real-life setting.

            Present your final decision in the following format:

            <decision>
            [Your choice of the most realistic dialog fragment. Answer with option name only.]
            </decision>
            """;
    }
}

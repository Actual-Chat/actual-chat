namespace ActualChat.Transcription;

/// <summary>
/// Static text templates used by <see cref="FakeTranscriber"/> to produce
/// deterministic fake transcripts in English and Russian.
/// </summary>
internal static class FakeTranscriberTemplates
{
    public static readonly string[] English = [
        // 1 — Pickles the cat
        "My cat Pickles has decided he is the rightful owner of this apartment, and frankly, "
        + "his case is strong. He has rearranged my keyboard, knocked over three mugs, and signed "
        + "off on a passive aggressive sigh every time I try to work. Yesterday he sat on the laptop "
        + "during a meeting, and my colleagues now believe I am a man with a fluffy beard. "
        + "He has demanded a tribute of tuna at the exact moment of seven a.m., reinforced by a paw "
        + "on the eyelid. I once tried to install a feeder, and he immediately filed a complaint "
        + "by destroying a roll of paper towels. The vet says he is healthy, confident, and possibly "
        + "a junior diplomat. My friends ask how my workout is going, and I report that I have lifted "
        + "Pickles, repeatedly, against his will, all day long. The lease is in my name, but the "
        + "apartment, by every measurable standard, belongs to a small animal who refuses to wear a collar.",
        // 2 — IKEA assembly
        "Last weekend I attempted to assemble an IKEA shelf called Vorndal, and I learned several "
        + "things about myself, including that I do not own enough Allen wrenches to be considered "
        + "a serious adult. The instructions were drawings of a small confused man being attacked by "
        + "lumber. I followed every step, then realized I had built it inside out, with the door panel "
        + "facing the wall like a guilty raccoon. My partner came home, looked at it, and said nothing "
        + "for a full minute, which was somehow worse than yelling. I disassembled the entire structure, "
        + "scattered the screws into a region of the floor I now call the Bermuda Triangle, and started "
        + "over. Three hours later the shelf stood proud and slightly crooked, like a soldier with one "
        + "cheerful leg. We loaded it with cookbooks, and immediately one shelf bowed downward. We named "
        + "it Vorndal anyway, because the box told us to, and we respect institutions.",
        // 3 — Smart fridge
        "Our smart fridge has begun ordering groceries on its own, which sounds modern until you realize "
        + "it has very strong opinions. Last week it ordered nine cucumbers because it could not see them "
        + "on the bottom shelf, where they were lounging behind a yogurt. It has subscribed us to a brand "
        + "of mustard that nobody in this household has ever tasted. The screen on the door now shows "
        + "motivational quotes, which I did not request, and it has started commenting on how often I "
        + "open it. I asked the manufacturer for help, and they said the fridge was learning my habits, "
        + "which felt accusatory. I replied that the fridge had judgments far beyond its station. I "
        + "unplugged it for ten minutes as a gesture of authority, and when I plugged it back in, it "
        + "ordered tofu. The cucumbers arrived in a refrigerated van, and the delivery driver gave me "
        + "a look that said he had seen this before, with other customers, on quieter streets.",
        // 4 — Lost keys
        "I lost my keys for the eighth time this year, and the search has reached the level of cinema. "
        + "I checked the usual suspects: the bowl, the couch, my own pockets, and the dog, who looked "
        + "offended. I retraced my steps from the previous evening, including the trip to the corner "
        + "store and a spontaneous detour to inspect a friendly cat. I walked the same route at the "
        + "same time of night, hoping to summon the keys back from wherever they were sleeping. I found "
        + "a pair of sunglasses I had forgotten existed, half a sandwich, and three dollars in change. "
        + "I did not find the keys. Eventually, I admitted defeat and called a locksmith, who arrived "
        + "with the unbothered expression of a man who has seen everything. He opened the door in "
        + "eighteen seconds and charged me as if it had taken hours. Two days later, I found the keys "
        + "in the freezer, between the peas and a frozen pizza I had also forgotten about.",
        // 5 — Office prank war
        "Our office prank war started small with a sticky note on a monitor and quickly escalated into "
        + "a quiet siege. Marketing replaced engineering's stapler with a stapler shaped chocolate. "
        + "Engineering retaliated by wrapping the entire marketing team's chairs in foil, including the "
        + "wheels, which now squeaked elegantly in three different keys. Then someone, no one will admit "
        + "who, switched all the keyboards to Dvorak overnight, and a senior designer wrote three pages "
        + "of nonsense before noticing. By Thursday, the printer was speaking French. The office manager "
        + "made an announcement about respect and the value of focus, but she was holding a rubber chicken "
        + "at the time, and it undermined her message. We agreed to a truce, signed in glitter pens, "
        + "witnessed by the office plant. The truce held for almost forty minutes before someone replaced "
        + "the elevator music with a slow dramatic version of the company anthem, played on a kazoo. We "
        + "are now told that all pranks must be filed in advance, in writing, in triplicate.",
        // 6 — Microwave rebellion
        "I tried to reheat soup yesterday, and the microwave declared independence. I pressed the buttons "
        + "in the usual order, and instead of beeping politely, it played a sequence of tones that sounded "
        + "suspiciously like a doorbell. I opened it, and the soup was somehow still cold, even though "
        + "the timer claimed two minutes had passed. I closed it again, and this time the lights inside "
        + "flickered like a small disco. The soup remained cold and now slightly nervous. I unplugged the "
        + "microwave, waited a respectful sixty seconds, and plugged it back in. Its display blinked at "
        + "me as if to say, you started this. I tried again, and the soup finally warmed up, but it had "
        + "absorbed the disco energy and tasted faintly of regret. The user manual was no help. The "
        + "internet was full of forums where people had similar experiences, all blaming a tiny capacitor "
        + "and a ghost. I have decided that the microwave and I will be civil but distant, like exes "
        + "who still share a kitchen.",
        // 7 — Squirrel general
        "There is a squirrel in my backyard who has declared himself emperor of the bird feeder. He "
        + "arrives at dawn, evicts the chickadees, and stuffs his face with sunflower seeds while glaring "
        + "at the cardinal as if the cardinal owes him money. I bought a squirrel proof feeder, and "
        + "within forty eight hours he had figured out the lever system. I bought a different model with "
        + "a spinning base, and he treated it like a carnival ride. He hangs upside down. He chews through "
        + "nylon. He has tactical opinions. I named him General Acorn. He has friends now, possibly "
        + "soldiers. They patrol the fence in shifts. I tried sprinkling cayenne on the seeds, which the "
        + "internet promised would deter him, and he ate the entire feeder with a slightly pleased "
        + "expression. The neighbor across the street says he saw the squirrel doing pull ups on the "
        + "lattice. I have surrendered the feeder. I am building a new one for the chickadees, somewhere "
        + "quieter, in a tree the general does not patrol.",
        // 8 — Coffee disaster
        "This morning I made coffee in the way only a sleep deprived person can. I scooped grounds into "
        + "the kettle instead of the filter basket, then poured cold water through the empty filter and "
        + "waited for the steam. I noticed nothing was happening, blamed the machine, and slapped its "
        + "side gently as one does. The kettle began to make a noise like a small whale. My partner "
        + "walked into the kitchen and began saying my name in a tone usually reserved for veterinarians. "
        + "I looked down at the kettle, which was now full of wet grounds and visible regret. I poured "
        + "the contents into a French press, because I am not a quitter, and what came out was something "
        + "we now call mud water. We drank it anyway. It had bite, character, and a faint flavor of "
        + "plastic. We arrived at work alert, anxious, and slightly faster than usual. The coffee machine "
        + "sat untouched on the counter, judging us. We have made peace, but we are not yet friends again.",
        // 9 — Pet rock startup
        "I have decided to start a pet rock business, which sounds like a terrible idea, and yet here we "
        + "are. I have selected ten rocks from my neighborhood with what I consider strong personalities. "
        + "I named them. I gave them small biographies. I built a website using a template that promised "
        + "it was easy and lied to me on three separate occasions. I am now charging twelve dollars per "
        + "rock, plus shipping, and the orders have started coming in, mostly from my mother and her "
        + "book club. The book club is intense, and they have asked for follow up rocks, which makes me "
        + "suspect they are reselling them. My most popular rock is named Walter, and he is a small, "
        + "slightly grumpy slate with what one customer described as quiet wisdom. Walter has a fan "
        + "account on social media. The fan account has more followers than I do. I am not sure how this "
        + "happened. My partner suggested I quit my actual job. I told her I was already halfway there.",
        // 10 — GPS detour
        "Yesterday my GPS routed me through a cornfield, and I trusted it because that is the deal we "
        + "have. The screen showed a road. The actual landscape showed corn. I drove forward, slowly, "
        + "with the cautious optimism of a man who has watched too many documentaries. The corn parted, "
        + "briefly, around a path that was clearly a tractor's idea, not a car's idea. My GPS continued "
        + "to insist that this was a road. A farmer waved at me from a distance, in the way farmers wave "
        + "when they are amused but also concerned for property. I reached a small clearing, and the GPS "
        + "said I had arrived at my destination, which was a wedding venue, and I was very late. The "
        + "wedding was beautiful. The bride did not mention the corn. My shoes had cobs in them for two "
        + "days. I have stopped trusting the GPS for shortcuts. We use it for highways now, and I never "
        + "let it suggest anything that involves the word path.",
    ];

    public static readonly string[] Russian = [
        // 1 — Канарейка Сёма
        "Бабушка моя завела канарейку, и теперь у нас в квартире происходит политический театр. "
        + "Канарейка по имени Сёма поёт только тогда, когда бабушка смотрит сериал, и ровно в самом "
        + "тревожном месте. Если героиня узнаёт плохую новость, Сёма выдаёт победный концерт, и сюжет "
        + "теряет всякий смысл. Бабушка пыталась его перевоспитывать, разговаривала с ним по-взрослому, "
        + "объясняла важность тишины. Сёма слушал, склонив голову, и пел опять. Потом бабушка купила "
        + "пластиковую птицу, чтобы ему было с кем общаться, и Сёма устроил настоящую драму. Он стучал "
        + "клювом по клетке, кидал семечки в пластикового друга и обиженно молчал три дня. Дед сказал, "
        + "что это политика. Бабушка сказала, что это характер. Я сказал, что это просто птица. Все "
        + "обиделись на меня. Сёма теперь поёт по расписанию, ровно в семь утра, ровно в семь вечера, "
        + "и в момент перехода на новый эпизод сериала. Это уже не птица, это диктор. Бабушка довольна. "
        + "Я тоже привык. Дед всё ещё считает, что это политика.",
        // 2 — Кот Боря
        "Сосед взял котёнка, а через неделю котёнок взял соседа. Маленький кот по имени Боря мгновенно "
        + "понял, что в квартире главный — он, и начал издавать постановления. Утром Боря звонит соседу "
        + "в десять минут шестого, толкая лапой стакан с водой. Сосед сначала пытался перевоспитать, "
        + "потом просто стал просыпаться сам, чтобы не было воды на полу. Днём Боря объявляет тихий час, "
        + "и если кто-то двигает мебель, он смотрит так, будто ему обещали покой и обманули. Я зашёл в "
        + "гости, и Боря оценил меня за двадцать секунд. Сел на ноги, посмотрел в глаза и решил, что я "
        + "тоже мебель. С тех пор я могу сидеть в этой квартире только так, как разрешит кот. Сосед "
        + "говорит, что у Бори сложный характер, но любящее сердце. Я говорю, что у Бори характер "
        + "начальника отдела. Сосед смеётся, но не возражает. На полке у соседа теперь лежит мисочка для "
        + "лакомств, и Боря лично проверяет её содержание трижды в день, как ревизор.",
        // 3 — Маршрутка
        "Маршрутка номер двадцать семь — это не транспорт, это спектакль. Водитель Сергей Михайлович "
        + "знает каждого пассажира в лицо и за полтора километра до остановки уже спрашивает, как у нас "
        + "дела. Если у тебя плохое настроение, он расскажет анекдот. Если у тебя хорошее настроение, "
        + "он расскажет другой анекдот. На лобовом стекле висит трёхцветная фигурка, кошечка, икона и "
        + "Чебурашка. Сергей Михайлович говорит, что они работают вместе. Однажды я забыл сдачу, он "
        + "догнал меня через два квартала, чтобы вернуть рубль. Однажды у бабушки рассыпалась картошка, "
        + "и весь салон собирал её, пока ехали. Музыка в маршрутке выбирается коллективно, голосованием "
        + "поднятыми руками. Сегодня выбрали Розенбаума, вчера — Высоцкого, на прошлой неделе — детский "
        + "хор. Кондуктора нет, потому что Сергей Михайлович считает, что взрослые люди сами знают, "
        + "сколько платить. Странным образом, никто не обманывает. На остановке возле рынка стоит "
        + "табличка, написанная от руки, с пожеланиями пассажиров. Я прочитал три и понял, что в этом "
        + "городе будет всё хорошо.",
        // 4 — Дача
        "Дача — это место, где взрослые становятся нервными детьми. У нас участок шесть соток, и на нём "
        + "в данный момент происходят три войны одновременно: с сорняками, с соседом и с собственной "
        + "спиной. Сорняки выигрывают, потому что их больше. Сосед выигрывает, потому что у него хитрый "
        + "забор. Спина пока не сдалась, но просит компромисса. Папа поставил теплицу, и теплица сразу "
        + "улетела во время первого ветра. Мы её ловили всем посёлком, и сосед потом неделю напоминал, "
        + "что его клубника пострадала. На самом деле клубника была в порядке, но это уже было не важно. "
        + "Мама посадила огурцы, и огурцы начали расти с удовольствием, как будто ждали этого момента "
        + "всю жизнь. Дед косит траву и одновременно поёт, и от этого трава растёт быстрее, я уверен. Я "
        + "еду на дачу с твёрдым намерением читать книгу, и каждый раз книга остаётся в сумке, "
        + "нечитанная. Зато я научился чинить водопровод, разговаривать с курицами и не пугать ёжика.",
        // 5 — Хомяк Никита
        "Хомячка зовут Никита, и он, кажется, политик. Утром Никита проводит инспекцию клетки, "
        + "переставляет миску, пересыпает наполнитель, и недовольно скрипит, если что-то лежит не там. "
        + "Днём он спит, демонстративно, в самой тёплой части дома. Вечером он начинает митинг. Никита "
        + "бегает в колесе с такой страстью, как будто он там не один, а с лозунгами. Колесо у нас "
        + "старое, и оно скрипит в трёх разных тональностях. Никита, по-видимому, сочиняет оперу. Соседи "
        + "снизу однажды поднялись и спросили, что у нас за станок. Я сказал, что это хомяк. Они "
        + "подумали, что я шучу. Я показал им Никиту. Они ушли молча. На следующий день мы получили от "
        + "соседей пакет морковки. Подозреваю, что это была мирная инициатива. Никита принял дары, "
        + "осмотрел их и положил в свой склад под колесом. Склад растёт. У него там запасы на зиму, на "
        + "три зимы, и небольшая коллекция семечек. Иногда я думаю, что в этом доме два хозяина, и у "
        + "одного из них четыре лапы и очень серьёзные намерения.",
        // 6 — Офисная война
        "В офисе у нас идёт тайная война, и никто не признаётся, но мы все участвуем. Сначала кто-то "
        + "подменил клавиатуру у Андрея на детскую, с большими буквами и весёлыми зверями. Андрей "
        + "сначала возмутился, потом сказал, что так проще печатать пятницу. Тогда мы решили подменить "
        + "его кружку на детскую, с поездом. Андрей пьёт чай из неё уже неделю и говорит, что чай стал "
        + "вкуснее. Затем кто-то заменил пароль на принтере, и принтер начал отказываться печатать всё, "
        + "что не имеет восклицательных знаков. Бухгалтерия страдала тихо, потому что бухгалтерия не "
        + "любит восклицательные знаки. На общем собрании директор попросил всех успокоиться, но при "
        + "этом стоял с резиновой уткой, потому что мы и его участок не пощадили. Утка кричала всякий "
        + "раз, когда он пытался её спрятать. Перемирие подписали в столовой, на салфетке, шариковой "
        + "ручкой, при свидетелях из охраны. Перемирие держалось полтора часа, потому что секретарь "
        + "обнаружила, что её цветок переставлен на другой стол. Война возобновилась, но теперь по "
        + "правилам.",
        // 7 — Старый телевизор
        "У бабушки дома стоит телевизор тысяча девятьсот восемьдесят какого-то года, и он работает. Не "
        + "просто работает, он выбирает, что показывать. Если бабушка хочет смотреть новости, телевизор "
        + "показывает кулинарную передачу. Если бабушка хочет смотреть кулинарную передачу, телевизор "
        + "показывает плохо настроенный канал, на котором поёт мужчина в синем пиджаке. Бабушка стучит "
        + "по нему ладонью, и иногда это помогает, а иногда телевизор обижается и показывает только "
        + "серую рябь. Тогда бабушка говорит ему ласковые слова, и телевизор оживает. Дед предлагал "
        + "купить новый, бабушка сказала, что новый не понимает её. Я попробовал заменить пульт, и "
        + "бабушка вернула пульт обратно через два часа. Мы решили его не трогать. Старый пульт "
        + "работает с тремя кнопками, остальные потерялись где-то между восьмидесятыми и девяностыми. "
        + "Антенна сделана из вешалки и фольги, и эта конструкция называется научная. Бабушка ловит на "
        + "ней восемнадцать каналов, и пять из них на украинском, и она утверждает, что понимает все. "
        + "На канале с поющим мужчиной у неё любимый сосед детства.",
        // 8 — Кошка Маруся
        "В нашем подъезде живёт кошка, которая никому не принадлежит и при этом всем нравится. Зовут "
        + "её, по неофициальной версии, Маруся. По другой версии, Маруся — это уже её четвёртая кличка, "
        + "потому что соседи периодически переименовывают её в зависимости от настроения. На первом "
        + "этаже она Маруся, на третьем — Багира, на пятом — Барон. Барон, по слухам, потому что в "
        + "прошлом году кто-то решил, что Маруся — мальчик. Маруся к этому относится спокойно. Она "
        + "лежит на подоконнике у окна и наблюдает за двором с видом главного редактора. Кто-то из "
        + "соседей принёс ей коробку, кто-то поставил миску, кто-то связал плед. Все три предмета она "
        + "использует. Управляющая компания пыталась её выселить, но соседи объединились и подписали "
        + "петицию, в которой было четырнадцать подписей и одна отпечатанная лапа. Управляющая компания "
        + "отступила. Теперь Маруся числится в неофициальных документах подъезда как смотритель. Я "
        + "однажды пришёл с букетом цветов, и она долго на меня смотрела, и я понял, что должен "
        + "здороваться правильно. С тех пор я говорю ей доброе утро.",
        // 9 — Конкурс грамотности
        "Племянник мой выступал на школьном конкурсе грамотности, и мы пришли всей семьёй болеть. Тема "
        + "была — слова с непроизносимыми согласными. Племянник пишет уверенно, и первый раунд прошёл "
        + "хорошо. Во втором раунде ему попалось слово чувствовать, и он на всякий случай спросил, "
        + "чувствовать что именно. Жюри засмеялось, но приняло вопрос. В третьем раунде ему попалось "
        + "слово сверстники, и тут случилась катастрофа. Племянник написал шверстники, потому что в "
        + "этот момент думал о шахматах. Бабушка из зала не выдержала и крикнула, что нужно думать о "
        + "словарях, а не о шахматах. Жюри сделало ей замечание. Дед сделал замечание жюри. Учительница "
        + "попросила всех успокоиться. Племянник продолжил соревнование, написал ещё пять слов "
        + "правильно и одно слово, которого не существует, но которое нам всем понравилось. Он назвал "
        + "его трепетение и объяснил, что это когда трепещешь и тренируешься одновременно. В жюри один "
        + "из членов кивнул и сказал, что слово красивое. Племянник занял третье место. Дома мы съели "
        + "торт, на котором было написано оптимизм, без буквы з.",
        // 10 — Магазин
        "Иду в магазин за хлебом, и магазин подвёл меня по-крупному. Хлеба нет. Стою у пустой полки, "
        + "как у мемориала. Продавщица говорит, что хлеб привезут, но не уточняет, в каком веке. Я "
        + "решил подождать. Подошёл за молоком, молока нет. Подошёл за маслом, масло есть, но другой "
        + "марки, той, которую никто не покупает, потому что она странного цвета. Я взял всё равно, "
        + "чтобы не уходить с пустыми руками. На кассе очередь из шести человек, и каждый второй "
        + "делится впечатлениями о пустых полках. Кассир молодая, она пытается работать быстро, но ей "
        + "мешает старушка в начале очереди, которая считает мелочь и расплачивается каждой монетой по "
        + "очереди. Спустя двадцать минут я наконец оплатил масло. На выходе встретил соседа, который "
        + "шёл за хлебом. Я сказал ему, что хлеба нет. Он сказал, что есть в другом магазине, через два "
        + "квартала. Мы пошли вместе. В другом магазине хлеб был, но не было молока. Мы рассмеялись. "
        + "Купили хлеб. Жизнь налажена. Масло осталось со мной, как сувенир.",
    ];

    public static readonly string[][] EnglishWords = English.Select(SplitWords).ToArray();
    public static readonly string[][] RussianWords = Russian.Select(SplitWords).ToArray();

    public static string[][] PickFor(Language language)
    {
        var v = language.Value;
        if (v.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return RussianWords;
        return EnglishWords;
    }

    private static string[] SplitWords(string template)
        => template.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

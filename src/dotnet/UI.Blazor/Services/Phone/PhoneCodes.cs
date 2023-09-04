namespace ActualChat.UI.Blazor.Services;

public sealed record PhoneCode(string Country, string DisplayCode)
{
    public string Code => Phone.Normalize(DisplayCode);
}

public static class PhoneCodes
{
    public static readonly PhoneCode Default = new ("United States of America", "+1");
    public static readonly List<PhoneCode> List = new () {
        new PhoneCode("Afghanistan", "+93"),
        new PhoneCode("Albania", "+355"),
        new PhoneCode("Algeria", "+213"),
        new PhoneCode("American Samoa", "+1 684"),
        new PhoneCode("Andorra", "+376"),
        new PhoneCode("Angola", "+244"),
        new PhoneCode("Anguilla", "+1 264"),
        new PhoneCode("Antarctica (Australian bases)", "+672 1"),
        new PhoneCode("Antigua and Barbuda", "+1 268"),
        new PhoneCode("Argentina", "+54"),
        new PhoneCode("Armenia", "+374"),
        new PhoneCode("Aruba", "+297"),
        new PhoneCode("Ascension", "+247"),
        new PhoneCode("Australia", "+61"),
        new PhoneCode("Austria", "+43"),
        new PhoneCode("Azerbaijan", "+994"),
        new PhoneCode("Bahamas", "+1 242"),
        new PhoneCode("Bahrain", "+973"),
        new PhoneCode("Bangladesh", "+880"),
        new PhoneCode("Barbados", "+1 246"),
        new PhoneCode("Belarus", "+375"),
        new PhoneCode("Belgium", "+32"),
        new PhoneCode("Belize", "+501"),
        new PhoneCode("Benin", "+229"),
        new PhoneCode("Bermuda", "+1 441"),
        new PhoneCode("Bhutan", "+975"),
        new PhoneCode("Bolivia", "+591"),
        new PhoneCode("Bonaire", "+599"),
        new PhoneCode("Bosnia and Herzegovina", "+387"),
        new PhoneCode("Botswana", "+267"),
        new PhoneCode("Brazil", "+55"),
        new PhoneCode("British Virgin Islands", "+1 284"),
        new PhoneCode("Brunei", "+673"),
        new PhoneCode("Bulgaria", "+359"),
        new PhoneCode("Burkina Faso", "+226"),
        new PhoneCode("Burundi", "+257"),
        new PhoneCode("Cabo Verde", "+238"),
        new PhoneCode("Cambodia", "+855"),
        new PhoneCode("Cameroon", "+237"),
        new PhoneCode("Canada", "+1"),
        new PhoneCode("Cayman Islands", "+1 345"),
        new PhoneCode("Central African Republic", "+236"),
        new PhoneCode("Chad", "+235"),
        new PhoneCode("Chile", "+56"),
        new PhoneCode("China", "+86"),
        new PhoneCode("Colombia", "+57"),
        new PhoneCode("Comoros", "+269"),
        new PhoneCode("Congo, Democratic Republic of the", "+243"),
        new PhoneCode("Congo, Republic of the", "+242"),
        new PhoneCode("Cook Islands", "+682"),
        new PhoneCode("Costa Rica", "+506"),
        new PhoneCode("Cote d'Ivoire", "+225"),
        new PhoneCode("Croatia", "+385"),
        new PhoneCode("Cuba", "+53"),
        new PhoneCode("Curaçao", "+599"),
        new PhoneCode("Cyprus", "+357"),
        new PhoneCode("Czech Republic", "+420"),
        new PhoneCode("Denmark", "+45"),
        new PhoneCode("Diego Garcia", "+246"),
        new PhoneCode("Djibouti", "+253"),
        new PhoneCode("Dominica", "+1 767"),
        new PhoneCode("Ecuador", "+593"),
        new PhoneCode("Egypt", "+20"),
        new PhoneCode("El Salvador", "+503"),
        new PhoneCode("Equatorial Guinea", "+240"),
        new PhoneCode("Eritrea", "+291"),
        new PhoneCode("Estonia", "+372"),
        new PhoneCode("Eswatini", "+268"),
        new PhoneCode("Ethiopia", "+251"),
        new PhoneCode("Falkland Islands", "+500"),
        new PhoneCode("Faroe Islands", "+298"),
        new PhoneCode("Fiji", "+679"),
        new PhoneCode("Finland", "+358"),
        new PhoneCode("France", "+33"),
        new PhoneCode("French Guiana", "+594"),
        new PhoneCode("French Polynesia", "+689"),
        new PhoneCode("Gabon", "+241"),
        new PhoneCode("Gambia", "+220"),
        new PhoneCode("Georgia", "+995"),
        new PhoneCode("Germany", "+49"),
        new PhoneCode("Ghana", "+233"),
        new PhoneCode("Gibraltar", "+350"),
        new PhoneCode("Greece", "+30"),
        new PhoneCode("Greenland", "+299"),
        new PhoneCode("Grenada", "+1 473"),
        new PhoneCode("Guadeloupe", "+590"),
        new PhoneCode("Guam", "+1 671"),
        new PhoneCode("Guatemala", "+502"),
        new PhoneCode("Guinea", "+224"),
        new PhoneCode("Guinea-Bissau", "+245"),
        new PhoneCode("Guyana", "+592"),
        new PhoneCode("Haiti", "+509"),
        new PhoneCode("Honduras", "+504"),
        new PhoneCode("Hong Kong", "+852"),
        new PhoneCode("Hungary", "+36"),
        new PhoneCode("Iceland", "+354"),
        new PhoneCode("India", "+91"),
        new PhoneCode("Indonesia", "+62"),
        new PhoneCode("Iran", "+98"),
        new PhoneCode("Iraq", "+964"),
        new PhoneCode("Ireland", "+353"),
        new PhoneCode("Israel", "+972"),
        new PhoneCode("Italy", "+39"),
        new PhoneCode("Japan", "+81"),
        new PhoneCode("Jordan", "+962"),
        new PhoneCode("Kazakhstan", "+7"),
        new PhoneCode("Kenya", "+254"),
        new PhoneCode("Kiribati", "+686"),
        new PhoneCode("Kosovo", "+383"),
        new PhoneCode("Kuwait", "+965"),
        new PhoneCode("Kyrgyzstan", "+996"),
        new PhoneCode("Laos", "+856"),
        new PhoneCode("Latvia", "+371"),
        new PhoneCode("Lebanon", "+961"),
        new PhoneCode("Lesotho", "+266"),
        new PhoneCode("Liberia", "+231"),
        new PhoneCode("Libya", "+218"),
        new PhoneCode("Liechtenstein", "+423"),
        new PhoneCode("Lithuania", "+370"),
        new PhoneCode("Luxembourg", "+352"),
        new PhoneCode("Macau", "+853"),
        new PhoneCode("Madagascar", "+261"),
        new PhoneCode("Malawi", "+265"),
        new PhoneCode("Malaysia", "+60"),
        new PhoneCode("Maldives", "+960"),
        new PhoneCode("Mali", "+223"),
        new PhoneCode("Malta", "+356"),
        new PhoneCode("Marshall Islands", "+692"),
        new PhoneCode("Martinique", "+596"),
        new PhoneCode("Mauritania", "+222"),
        new PhoneCode("Mauritius", "+230"),
        new PhoneCode("Mayotte", "+262"),
        new PhoneCode("Mexico", "+52"),
        new PhoneCode("Micronesia, Federated States of", "+691"),
        new PhoneCode("Moldova", "+373"),
        new PhoneCode("Monaco", "+377"),
        new PhoneCode("Mongolia", "+976"),
        new PhoneCode("Montenegro", "+382"),
        new PhoneCode("Montserrat", "+1 664"),
        new PhoneCode("Morocco", "+212"),
        new PhoneCode("Mozambique", "+258"),
        new PhoneCode("Myanmar", "+95"),
        new PhoneCode("Namibia", "+264"),
        new PhoneCode("Nauru", "+674"),
        new PhoneCode("Nepal", "+977"),
        new PhoneCode("Netherlands", "+31"),
        new PhoneCode("New Caledonia", "+687"),
        new PhoneCode("New Zealand", "+64"),
        new PhoneCode("Nicaragua", "+505"),
        new PhoneCode("Niger", "+227"),
        new PhoneCode("Nigeria", "+234"),
        new PhoneCode("Niue", "+683"),
        new PhoneCode("Norfolk Island", "+672 3"),
        new PhoneCode("North Korea", "+850"),
        new PhoneCode("North Macedonia", "+389"),
        new PhoneCode("Northern Mariana Islands", "+1 670"),
        new PhoneCode("Norway", "+47"),
        new PhoneCode("Oman", "+968"),
        new PhoneCode("Pakistan", "+92"),
        new PhoneCode("Palau", "+680"),
        new PhoneCode("Palestine", "+970"),
        new PhoneCode("Panama", "+507"),
        new PhoneCode("Papua New Guinea", "+675"),
        new PhoneCode("Paraguay", "+595"),
        new PhoneCode("Peru", "+51"),
        new PhoneCode("Philippines", "+63"),
        new PhoneCode("Poland", "+48"),
        new PhoneCode("Portugal", "+351"),
        new PhoneCode("Qatar", "+974"),
        new PhoneCode("Reunion", "+262"),
        new PhoneCode("Romania", "+40"),
        new PhoneCode("Russia", "+7"),
        new PhoneCode("Rwanda", "+250"),
        new PhoneCode("Saba", "+599"),
        new PhoneCode("Saint-Barthelemy", "+590"),
        new PhoneCode("Saint Helena", "+290"),
        new PhoneCode("Saint Kitts and Nevis", "+1 869"),
        new PhoneCode("Saint Lucia", "+1 758"),
        new PhoneCode("Saint Martin (French side)", "+590"),
        new PhoneCode("Saint Pierre and Miquelon", "+508"),
        new PhoneCode("Saint Vincent and the Grenadines", "+1 784"),
        new PhoneCode("Samoa", "+685"),
        new PhoneCode("San Marino", "+378"),
        new PhoneCode("Sao Tome and Principe", "+239"),
        new PhoneCode("Saudi Arabia", "+966"),
        new PhoneCode("Senegal", "+221"),
        new PhoneCode("Serbia", "+381"),
        new PhoneCode("Seychelles", "+248"),
        new PhoneCode("Sierra Leone", "+232"),
        new PhoneCode("Singapore", "+65"),
        new PhoneCode("Sint Eustatius", "+599"),
        new PhoneCode("Sint Maarten (Dutch side)", "+1 721"),
        new PhoneCode("Slovakia", "+421"),
        new PhoneCode("Slovenia", "+386"),
        new PhoneCode("Solomon Islands", "+677"),
        new PhoneCode("Somalia", "+252"),
        new PhoneCode("South Africa", "+27"),
        new PhoneCode("South Korea", "+82"),
        new PhoneCode("South Sudan", "+211"),
        new PhoneCode("Spain", "+34"),
        new PhoneCode("Sri Lanka", "+94"),
        new PhoneCode("Sudan", "+249"),
        new PhoneCode("Suriname", "+597"),
        new PhoneCode("Sweden", "+46"),
        new PhoneCode("Switzerland", "+41"),
        new PhoneCode("Syria", "+963"),
        new PhoneCode("Taiwan", "+886"),
        new PhoneCode("Tajikistan", "+992"),
        new PhoneCode("Tanzania", "+255"),
        new PhoneCode("Thailand", "+66"),
        new PhoneCode("Timor-Leste", "+670"),
        new PhoneCode("Togo", "+228"),
        new PhoneCode("Tokelau", "+690"),
        new PhoneCode("Tonga", "+676"),
        new PhoneCode("Trinidad and Tobago", "+1 868"),
        new PhoneCode("Tristan da Cunha", "+290"),
        new PhoneCode("Tunisia", "+216"),
        new PhoneCode("Turkey", "+90"),
        new PhoneCode("Turkmenistan", "+993"),
        new PhoneCode("Turks and Caicos Islands", "+1 649"),
        new PhoneCode("Tuvalu", "+688"),
        new PhoneCode("Uganda", "+256"),
        new PhoneCode("Ukraine", "+380"),
        new PhoneCode("United Arab Emirates", "+971"),
        new PhoneCode("United Kingdom", "+44"),
        new PhoneCode("United States of America", "+1"),
        new PhoneCode("Uruguay", "+598"),
        new PhoneCode("Uzbekistan", "+998"),
        new PhoneCode("Vanuatu", "+678"),
        new PhoneCode("Vatican City State", "+39"),
        new PhoneCode("Venezuela", "+58"),
        new PhoneCode("Vietnam", "+84"),
        new PhoneCode("U.S. Virgin Islands", "+1 340"),
        new PhoneCode("Wallis and Futuna", "+681"),
        new PhoneCode("Yemen", "+967"),
        new PhoneCode("Zambia", "+260"),
        new PhoneCode("Zimbabwe", "+263"),
    };
    public static readonly int MaxCodeLength = List.Max(x => x.Code.Length);

    private static readonly Dictionary<string, PhoneCode> _byCode =
        // distinct cause some codes duplicate
        List.Distinct(PhoneCodeComparer.Instance).ToDictionary(x => x.Code, PhoneCodeComparer.Instance);

    public static PhoneCode? GetByCode(string codeOrDisplayCode)
        => _byCode.GetValueOrDefault(codeOrDisplayCode);
}

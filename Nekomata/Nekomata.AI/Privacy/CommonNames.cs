namespace Nekomata.AI.Privacy;

// Used to catch people who aren't in the directory yet (a new starter on a ticket, an outside contact in an email):
// a common first name followed by a capitalised word is treated as a person.
internal static class CommonNames
{
    /// <summary>First names that are also everyday words; never matched on their own.</summary>
    public static readonly HashSet<string> AmbiguousWords = new(StringComparer.Ordinal)
    {
        "Will", "May", "Mark", "Bill", "Grace", "Hope", "Joy", "June", "April", "Rose", "Faith", "Dawn", "Sky", "Summer",
        "Amber", "Ray", "Rich", "Frank", "Art", "Holly", "Ivy",
        "Iris", "Jade", "Ruby", "Pearl", "Daisy", "Poppy", "Robin", "Jay", "Lane", "Gene", "Guy", "Earl", "Cliff",
        "Brook", "Chase", "Hunter", "Mason", "Page", "Penny", "Royal", "Sterling", "Star", "Tank", "Don",
        "Angel", "Autumn", "Crystal", "Destiny", "Harmony", "Heather", "Willow", "Clay",
        "Drew", "Miles", "Wade", "Reed", "Grant", "Basil", "Sage", "Storm", "Patience", "Charity", "Major",
        "Prince", "Duke", "King", "Stone", "Ash", "Bay", "Cash", "Fern", "Forest", "Gray", "Grey", "Haven", "Sunny", "Rain",
        "Echo", "Lily", "Asia", "Paris", "Florence", "Georgia", "Chelsea", "Sydney", "Phoenix", "Jordan", "Austin", "Dallas",
        "Microsoft", "Windows", "Office", "Teams", "Outlook", "Azure", "Guardian", "Nekomata", "Monday", "Tuesday",
        "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "January", "February", "March", "August", "September",
        "October", "November", "December", "IT", "Service", "Support", "Team", "Admin", "Reception", "Accounts", "Sales",
        "Warehouse", "Finance", "Customer", "Manager", "Director",
    };

    /// <summary>Capitalised words that are not surnames after a first name ("Sam Thursday", "Jane Microsoft").</summary>
    public static readonly HashSet<string> NotSurnames = new(StringComparer.Ordinal)
    {
        "The", "And", "Or", "But", "If", "Is", "Are", "Was", "Has", "Had", "Will", "Can", "Could", "Should", "Would",
        "From", "To", "For", "On", "In", "At", "By", "With", "About", "Re", "Fw", "Fwd", "Hi", "Hello", "Thanks", "Team",
        "IT", "Support", "Service", "Desk", "Office", "Microsoft", "Windows", "Teams", "Outlook", "Excel", "Word",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "January", "February", "March",
        "April", "May", "June", "July", "August", "September", "October", "November", "December", "Please", "Can't",
        "Ticket", "Laptop", "Printer", "Phone", "Email", "Account", "Password", "Licence", "License", "Update", "Today",
        "Tomorrow", "Yesterday", "Morning", "Afternoon", "Mr", "Mrs", "Ms", "Dr", "Ltd", "Limited", "PLC", "Group",
    };

    /// <summary>Common UK first names (ambiguous ones excluded).</summary>
    public static readonly HashSet<string> FirstNames = new(StringComparer.Ordinal)
    {
        "Aaron", "Abby", "Abigail", "Adam", "Adrian", "Aidan", "Aiden", "Alan", "Alana", "Alastair", "Albert", "Alec", "Alex",
        "Alexander", "Alexandra", "Alfie", "Alice", "Alicia", "Alison", "Alistair", "Allan", "Allison", "Amanda", "Amelia",
        "Amy", "Andrea", "Andrew", "Andy", "Angela", "Angus", "Anita", "Ann", "Anna", "Anne", "Annie", "Anthony", "Antony",
        "Archie", "Arthur", "Ashley", "Barbara", "Barry", "Becky", "Bella", "Benjamin", "Beth", "Bethany", "Betty", "Beverley",
        "Billy", "Brandon", "Brenda", "Brian", "Bryan", "Callum", "Calum", "Cameron", "Caroline", "Carl", "Carla", "Carly",
        "Carole", "Caitlin", "Catherine", "Charlie", "Charles", "Charlotte", "Chloe", "Chris", "Christian", "Christina",
        "Christine", "Christopher", "Claire", "Clare", "Colin", "Connor", "Conor", "Craig", "Daniel", "Danielle", "Danny",
        "Darren", "Dave", "David", "Debbie", "Deborah", "Declan", "Denise", "Dennis", "Derek", "Diane", "Dominic", "Donna",
        "Dylan", "Edward", "Eileen", "Elaine", "Eleanor", "Elizabeth", "Ella", "Ellie", "Emily", "Emma", "Eric", "Erin",
        "Ethan", "Evan", "Fiona", "Finlay", "Finn", "Frances", "Francesca", "Freddie", "Freya", "Gary", "Gavin", "Gemma",
        "Geoff", "Geoffrey", "George", "Gerald", "Gillian", "Gordon", "Graham", "Greg", "Gregory", "Hannah", "Harry",
        "Harriet", "Harvey", "Hayley", "Helen", "Henry", "Hollie", "Ian", "Imogen", "Isaac", "Isabel", "Isabella", "Isabelle",
        "Isla", "Jack", "Jacob", "Jacqueline", "Jake", "James", "Jamie", "Jane", "Janet", "Janice", "Jasmine", "Jason",
        "Jean", "Jeff", "Jeffrey", "Jemma", "Jenna", "Jennifer", "Jenny", "Jeremy", "Jessica", "Jill", "Jim", "Jo", "Joan",
        "Joanna", "Joanne", "Jodie", "Joe", "Joel", "John", "Jonathan", "Jon", "Joseph", "Josh", "Joshua", "Judith", "Julia",
        "Julian", "Julie", "Justin", "Karen", "Kate", "Katherine", "Kathryn", "Katie", "Kayleigh", "Keith", "Kelly", "Kenneth",
        "Kerry", "Kevin", "Kev", "Kieran", "Kim", "Kirsty", "Laura", "Lauren", "Leah", "Leanne", "Lewis", "Liam", "Linda",
        "Lindsay", "Lisa", "Liz", "Logan", "Lorna", "Lorraine", "Louis", "Louise", "Lucas", "Lucy", "Luke", "Lynn", "Lynne",
        "Maria", "Marie", "Martin", "Martyn", "Mary", "Matt", "Matthew", "Megan", "Melanie", "Melissa", "Michael", "Michelle",
        "Mike", "Mohammed", "Muhammad", "Molly", "Nadia", "Natalie", "Natasha", "Nathan", "Nicholas", "Nick", "Nicola",
        "Nicole", "Nigel", "Noah", "Oliver", "Olivia", "Oscar", "Owen", "Pamela", "Paul", "Paula", "Pauline", "Peter",
        "Phil", "Philip", "Phillip", "Rachel", "Rebecca", "Richard", "Rachael", "Rhys", "Robert", "Roger", "Ronald", "Ross",
        "Russell", "Ruth", "Ryan", "Sally", "Sam", "Samantha", "Samuel", "Sandra", "Sara", "Sarah", "Scott", "Sean", "Shane",
        "Sharon", "Shaun", "Sheila", "Simon", "Sophia", "Sophie", "Stacey", "Stephanie", "Stephen", "Steve", "Steven",
        "Stuart", "Susan", "Suzanne", "Tara", "Terry", "Theo", "Thomas", "Tim", "Timothy", "Toby", "Tony", "Tracey", "Tracy",
        "Trevor", "Tyler", "Valerie", "Vanessa", "Vicky", "Victoria", "Wayne", "William", "Yvonne", "Zoe", "Zara", "Tom", "Ben", "Rob", "Bob", "Lee", "Neil", "Dean", "Max", "Sue", "Carol", "Glen", "Ali", "Victor", "Ahmed",
        "Aisha", "Amir", "Fatima", "Hassan", "Priya", "Raj", "Ravi", "Sanjay", "Sunil", "Anil", "Deepak", "Imran", "Omar",
        "Yusuf", "Zain", "Piotr", "Tomasz", "Marek", "Agnieszka", "Katarzyna", "Anna-Marie", "Siobhan", "Niamh", "Aoife",
        "Ciara", "Sinead", "Ewan", "Euan", "Fraser", "Hamish", "Iain", "Kirsten", "Morag", "Rhian", "Sian", "Gareth", "Dafydd",
    };
}

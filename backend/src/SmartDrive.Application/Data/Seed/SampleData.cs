using SmartDrive.Domain.Entities;

namespace SmartDrive.Application.Data.Seed;

/// <summary>
/// Reference data for the demonstration fleet. Every record created from here is flagged
/// <c>IsSampleData = true</c> and labelled as sample data in the interface.
///
/// Vehicle specifications are approximate, manufacturer-style figures chosen to make the demo
/// realistic. They are sample data, not verified product information.
/// </summary>
internal static class SampleData
{
    public sealed record CategorySeed(string Name, string Slug, string Description, int SortOrder);

    public sealed record LocationSeed(string Name, string Code, string AddressLine, string City, string Province, bool IsAirport);

    public sealed record VehicleSeed(
        string Slug,
        string Make,
        string Model,
        int Year,
        string Category,
        string HomeLocationCode,
        string Registration,
        int Seats,
        int Doors,
        int Luggage,
        TransmissionType Transmission,
        FuelType Fuel,
        decimal? LitresPer100Km,
        int Comfort,
        decimal DailyRate,
        decimal Deposit,
        int? MileageLimitKm,
        decimal? ExcessPerKm,
        string Colour,
        string Shape,
        string Description,
        string[] Features,
        double Demand);

    public static readonly CategorySeed[] Categories =
    [
        new("Economy", "economy", "Small, frugal cars for getting around town on a tight budget.", 1),
        new("Hatchback", "hatchback", "Practical five-door hatchbacks that are easy to park and cheap to run.", 2),
        new("Sedan", "sedan", "Comfortable sedans with a separate boot, ideal for business trips.", 3),
        new("SUV", "suv", "Higher-riding vehicles with extra space for families and longer journeys.", 4),
        new("Minivan", "minivan", "Seven to nine seats for groups, families and airport transfers.", 5),
        new("Bakkie", "bakkie", "Double-cab bakkies with a load bin for gear, tools and gravel roads.", 6),
        new("Luxury", "luxury", "Premium sedans for special occasions and executive travel.", 7),
    ];

    public static readonly LocationSeed[] Locations =
    [
        new("OR Tambo International Airport", "JNB", "Car rental desks, arrivals level, Kempton Park", "Johannesburg", "Gauteng", true),
        new("Sandton City", "SDT", "Rivonia Road, Sandhurst", "Johannesburg", "Gauteng", false),
        new("Cape Town International Airport", "CPT", "Car rental desks, central terminal, Matroosfontein", "Cape Town", "Western Cape", true),
        new("Cape Town City Centre", "CTC", "Strand Street, Cape Town City Centre", "Cape Town", "Western Cape", false),
        new("King Shaka International Airport", "DUR", "Car rental desks, arrivals, La Mercy", "Durban", "KwaZulu-Natal", true),
        new("Chief Dawid Stuurman International Airport", "PLZ", "Car rental desks, arrivals, Walmer", "Gqeberha", "Eastern Cape", true),
    ];

    private static readonly string[] Basics = ["Air conditioning", "Bluetooth audio", "USB charging"];

    public static readonly VehicleSeed[] Vehicles =
    [
        new("renault-kwid", "Renault", "Kwid 1.0 Dynamique", 2024, "economy", "JNB", "JN 41 KW GP", 5, 5, 1,
            TransmissionType.Manual, FuelType.Petrol, 4.7m, 2, 429m, 2500m, null, null, "#e8eef3", "hatch",
            "A light, easy-to-drive city car with very low running costs.", [.. Basics], 0.55),
        new("suzuki-celerio", "Suzuki", "Celerio 1.0 GL", 2024, "economy", "CPT", "CA 518-204", 5, 5, 1,
            TransmissionType.Manual, FuelType.Petrol, 4.4m, 2, 449m, 2500m, null, null, "#c8102e", "hatch",
            "One of the most fuel-efficient petrol cars on the road, with surprising cabin space.", [.. Basics], 0.6),
        new("vw-polo-vivo", "Volkswagen", "Polo Vivo 1.4 Life", 2024, "hatchback", "JNB", "JP 27 VV GP", 5, 5, 2,
            TransmissionType.Manual, FuelType.Petrol, 5.7m, 3, 549m, 3000m, null, null, "#1f3a5f", "hatch",
            "South Africa's favourite hatchback: solid, simple and comfortable on the highway.", [.. Basics, "Cruise control"], 0.65),
        new("suzuki-swift", "Suzuki", "Swift 1.2 GL CVT", 2025, "hatchback", "DUR", "ND 640-115", 5, 5, 1,
            TransmissionType.Automatic, FuelType.Petrol, 4.9m, 3, 579m, 3000m, null, null, "#f2a900", "hatch",
            "A nimble automatic hatchback that makes city traffic effortless.", [.. Basics, "Apple CarPlay and Android Auto"], 0.6),
        new("toyota-starlet", "Toyota", "Starlet 1.5 Xs AT", 2024, "hatchback", "CTC", "CA 733-562", 5, 5, 2,
            TransmissionType.Automatic, FuelType.Petrol, 5.4m, 3, 599m, 3000m, null, null, "#9aa5ad", "hatch",
            "A roomy automatic hatchback with a generous boot for its size.", [.. Basics, "Reverse camera"], 0.6),
        new("hyundai-i20", "Hyundai", "i20 1.2 Motion", 2023, "hatchback", "PLZ", "HGT 482 EC", 5, 5, 2,
            TransmissionType.Manual, FuelType.Petrol, 5.6m, 3, 559m, 3000m, null, null, "#2e7d6b", "hatch",
            "A well-equipped hatchback with a comfortable ride on longer trips.", [.. Basics, "Apple CarPlay and Android Auto"], 0.5),
        new("byd-dolphin", "BYD", "Dolphin Dynamic", 2025, "hatchback", "CTC", "CA 902-317", 5, 5, 2,
            TransmissionType.Automatic, FuelType.Electric, null, 4, 849m, 5000m, null, null, "#4fb3bf", "hatch",
            "A fully electric hatchback with a quiet, smooth drive. Charging cable included.", [.. Basics, "Rotating touchscreen", "Type 2 charging cable"], 0.35),
        new("toyota-corolla-hybrid", "Toyota", "Corolla 1.8 Hybrid XS", 2024, "sedan", "SDT", "JS 18 HY GP", 5, 4, 3,
            TransmissionType.Automatic, FuelType.Hybrid, 4.5m, 4, 899m, 4000m, null, null, "#f4f4f2", "sedan",
            "A refined hybrid sedan: quiet, economical and comfortable for business travel.", [.. Basics, "Adaptive cruise control", "Lane keeping assist"], 0.5),
        new("honda-city", "Honda", "City 1.5 Elegance CVT", 2025, "sedan", "DUR", "ND 271-908", 5, 4, 3,
            TransmissionType.Automatic, FuelType.Petrol, 5.6m, 4, 749m, 4000m, null, null, "#6b1f2a", "sedan",
            "A spacious compact sedan with a large boot and a comfortable rear seat.", [.. Basics, "Reverse camera"], 0.45),
        new("toyota-corolla-cross", "Toyota", "Corolla Cross 1.8 XS Hybrid", 2024, "suv", "JNB", "JC 55 XS GP", 5, 5, 3,
            TransmissionType.Automatic, FuelType.Hybrid, 4.3m, 4, 999m, 5000m, null, null, "#5c6670", "suv",
            "A frugal hybrid SUV with a raised driving position and space for a family's luggage.", [.. Basics, "Adaptive cruise control", "Reverse camera"], 0.75),
        new("haval-jolion", "Haval", "Jolion 1.5T Super Luxury", 2024, "suv", "CPT", "CA 615-440", 5, 5, 3,
            TransmissionType.Automatic, FuelType.Petrol, 7.4m, 4, 949m, 5000m, null, null, "#d9dde0", "suv",
            "A comfortable, well-equipped SUV with a panoramic roof and plenty of rear legroom.", [.. Basics, "Panoramic sunroof", "360° camera"], 0.7),
        new("chery-tiggo-4", "Chery", "Tiggo 4 Pro 1.5 LIT CVT", 2025, "suv", "SDT", "JT 46 CP GP", 5, 5, 2,
            TransmissionType.Automatic, FuelType.Petrol, 7.0m, 4, 849m, 5000m, null, null, "#3d5a80", "suv",
            "A compact automatic SUV with a modern cabin and good value for money.", [.. Basics, "Wireless phone charging"], 0.65),
        new("vw-t-cross", "Volkswagen", "T-Cross 1.0 TSI Life DSG", 2024, "suv", "CTC", "CA 377-091", 5, 5, 3,
            TransmissionType.Automatic, FuelType.Petrol, 5.9m, 4, 899m, 5000m, null, null, "#b34a2b", "suv",
            "A small SUV with a sliding rear bench, so you can trade legroom for boot space.", [.. Basics, "Apple CarPlay and Android Auto"], 0.6),
        new("toyota-fortuner", "Toyota", "Fortuner 2.8 GD-6 4x2 AT", 2023, "suv", "JNB", "JF 28 GD GP", 7, 5, 4,
            TransmissionType.Automatic, FuelType.Diesel, 8.0m, 4, 1599m, 8000m, null, null, "#2b2d30", "suv",
            "A rugged seven-seat SUV built for long road trips and gravel roads.", [.. Basics, "Third-row seating", "Tow bar"], 0.7),
        new("toyota-rumion", "Toyota", "Rumion 1.5 TX AT", 2024, "minivan", "DUR", "ND 830-662", 7, 5, 2,
            TransmissionType.Automatic, FuelType.Petrol, 6.0m, 3, 899m, 5000m, null, null, "#8c96a0", "van",
            "An affordable seven-seater for families, with flexible seating.", [.. Basics, "Third-row seating"], 0.5),
        new("hyundai-staria", "Hyundai", "Staria 2.2D Executive 9-seater", 2023, "minivan", "JNB", "JH 92 ST GP", 9, 4, 5,
            TransmissionType.Automatic, FuelType.Diesel, 8.9m, 4, 1899m, 10000m, null, null, "#e6e8ea", "van",
            "A nine-seat people carrier with sliding doors, ideal for groups and airport transfers.", [.. Basics, "Sliding side doors", "Rear climate control"], 0.45),
        new("toyota-hilux", "Toyota", "Hilux 2.4 GD-6 Raider Double Cab", 2023, "bakkie", "PLZ", "HGB 215 EC", 5, 4, 4,
            TransmissionType.Manual, FuelType.Diesel, 7.4m, 3, 1099m, 8000m, null, null, "#f5f5f5", "bakkie",
            "A dependable double-cab bakkie with a load bin for camping gear, tools or bikes.", [.. Basics, "Load bin with tonneau cover", "Tow bar"], 0.55),
        new("ford-ranger", "Ford", "Ranger 2.0 XLT Double Cab AT", 2024, "bakkie", "CPT", "CA 244-718", 5, 4, 4,
            TransmissionType.Automatic, FuelType.Diesel, 7.6m, 4, 1249m, 8000m, null, null, "#1c4e80", "bakkie",
            "A comfortable automatic bakkie that is as happy on the highway as on a farm road.", [.. Basics, "Load bin with roller shutter", "Reverse camera"], 0.55),
        new("bmw-320i", "BMW", "320i M Sport", 2024, "luxury", "SDT", "JB 32 MS GP", 5, 4, 3,
            TransmissionType.Automatic, FuelType.Petrol, 6.4m, 5, 1899m, 10000m, 200, 3.50m, "#101820", "sedan",
            "A sporty executive sedan with a premium cabin. Includes 200 km per day.", [.. Basics, "Leather seats", "Head-up display", "Adaptive cruise control"], 0.3),
        new("mercedes-c200", "Mercedes-Benz", "C200", 2024, "luxury", "CPT", "CA 200-583", 5, 4, 3,
            TransmissionType.Automatic, FuelType.Petrol, 6.6m, 5, 1999m, 10000m, 200, 3.50m, "#7a8189", "sedan",
            "A quiet, beautifully finished luxury sedan. Includes 200 km per day.", [.. Basics, "Leather seats", "Ambient lighting", "Adaptive cruise control"], 0.3),
        new("vw-polo-sedan", "Volkswagen", "Polo Sedan 1.6 Comfortline", 2023, "sedan", "PLZ", "HGS 619 EC", 5, 4, 3,
            TransmissionType.Manual, FuelType.Petrol, 6.1m, 3, 649m, 3500m, null, null, "#3b3f45", "sedan",
            "An economical sedan with a large boot, well suited to long-distance driving.", [.. Basics, "Cruise control"], 0.45),
    ];

    public static readonly (string First, string Last, string City, string Province)[] Customers =
    [
        ("Thabo", "Nkosi", "Johannesburg", "Gauteng"), ("Lerato", "Mokoena", "Pretoria", "Gauteng"),
        ("Sipho", "Dlamini", "Durban", "KwaZulu-Natal"), ("Ayesha", "Patel", "Johannesburg", "Gauteng"),
        ("Johan", "van der Merwe", "Stellenbosch", "Western Cape"), ("Naledi", "Khumalo", "Soweto", "Gauteng"),
        ("Pieter", "Botha", "Bloemfontein", "Free State"), ("Zanele", "Mthembu", "Pietermaritzburg", "KwaZulu-Natal"),
        ("Kagiso", "Molefe", "Rustenburg", "North West"), ("Fatima", "Davids", "Cape Town", "Western Cape"),
        ("Ruan", "Pretorius", "Gqeberha", "Eastern Cape"), ("Nomvula", "Zulu", "Durban", "KwaZulu-Natal"),
        ("Mpho", "Sithole", "Polokwane", "Limpopo"), ("Chantal", "Jacobs", "Paarl", "Western Cape"),
        ("Bongani", "Ndlovu", "Mbombela", "Mpumalanga"), ("Priya", "Naidoo", "Umhlanga", "KwaZulu-Natal"),
        ("Andile", "Cele", "East London", "Eastern Cape"), ("Karabo", "Mahlangu", "Centurion", "Gauteng"),
        ("Lindiwe", "Ngcobo", "Durban", "KwaZulu-Natal"), ("Tshepo", "Maseko", "Johannesburg", "Gauteng"),
        ("Yusuf", "Ebrahim", "Cape Town", "Western Cape"), ("Megan", "Smith", "Knysna", "Western Cape"),
        ("Kamogelo", "Tau", "Mahikeng", "North West"), ("Ntombi", "Shabalala", "Newcastle", "KwaZulu-Natal"),
        ("Willem", "Steyn", "George", "Western Cape"), ("Refilwe", "Modise", "Kimberley", "Northern Cape"),
        ("Siyabonga", "Mkhize", "Durban", "KwaZulu-Natal"), ("Amahle", "Gumede", "Gqeberha", "Eastern Cape"),
        ("Dineo", "Phiri", "Johannesburg", "Gauteng"), ("Jacques", "du Plessis", "Cape Town", "Western Cape"),
        ("Nadia", "Hendricks", "Cape Town", "Western Cape"), ("Lungile", "Mabuza", "Mbombela", "Mpumalanga"),
        ("Tumelo", "Kekana", "Pretoria", "Gauteng"), ("Busisiwe", "Ntuli", "Richards Bay", "KwaZulu-Natal"),
        ("Rajesh", "Pillay", "Durban", "KwaZulu-Natal"), ("Sarah", "Adams", "Makhanda", "Eastern Cape"),
    ];

    /// <summary>Relative demand by calendar month (December holidays, Easter and July school holidays are busier).</summary>
    public static double Season(int month) => month switch
    {
        12 => 1.6,
        1 => 1.3,
        3 => 1.05,
        4 => 1.3,
        6 => 1.15,
        7 => 1.3,
        9 => 1.05,
        2 => 0.8,
        5 => 0.85,
        8 => 0.9,
        _ => 1.0,
    };

    public static readonly (string Theme, string[] Good, string[] Bad)[] ReviewThemes =
    [
        ("cleanliness",
            ["The car was spotless when we collected it.", "Interior was very clean and smelled fresh.", "Everything was clean and tidy inside."],
            ["The car was dirty inside when we collected it.", "There was sand and litter on the back seat.", "The boot was not cleaned before our rental."]),
        ("comfort",
            ["Very comfortable seats on the long drive.", "A smooth and quiet ride, even on the N2.", "Plenty of legroom for everyone."],
            ["The seats became uncomfortable after a few hours.", "Road noise was loud on the highway.", "Quite cramped for four adults."]),
        ("condition",
            ["The vehicle felt new and drove perfectly.", "Tyres and brakes were in great condition.", "Everything worked as it should."],
            ["A warning light came on during the trip.", "The air-con was not cooling properly.", "The tyres looked worn."]),
        ("service",
            ["Friendly and helpful staff at the desk.", "The team was quick and professional.", "Great service from start to finish."],
            ["We waited a long time at the counter.", "Staff were unhelpful when we had a question.", "Customer service was slow to respond."]),
        ("booking",
            ["Booking online was quick and easy.", "Pickup was fast and the paperwork simple.", "The booking process was smooth."],
            ["The booking confirmation was confusing.", "Pickup took longer than expected.", "Returning the car was a slow process."]),
        ("value",
            ["Good value for money.", "Fair price for a great car.", "Worth every rand."],
            ["A bit expensive for what you get.", "Poor value compared to other options.", "The extra fees felt too high."]),
    ];

    public static readonly string[] PositiveTitles = ["Great experience", "Would rent again", "Excellent car", "Smooth trip", "Highly recommended", "Very happy"];
    public static readonly string[] MixedTitles = ["Good, with a few issues", "Decent rental", "Mostly fine", "Okay overall"];
    public static readonly string[] NegativeTitles = ["Disappointing", "Needs improvement", "Not great", "Below expectations"];

    public static readonly (MaintenanceType Type, string Title, string Description)[] ReportedIssues =
    [
        (MaintenanceType.Repair, "Reported: engine warning light", "Customer reported an engine warning light during the rental. Diagnostics and sensor replaced."),
        (MaintenanceType.Repair, "Reported: air-con not cooling", "Customer reported weak air conditioning. Re-gassed and checked for leaks."),
        (MaintenanceType.Tyres, "Reported: slow puncture", "Customer reported a tyre pressure warning. Puncture repaired and tyres checked."),
        (MaintenanceType.Repair, "Reported: brake noise", "Customer reported squealing brakes. Front pads replaced."),
    ];
}

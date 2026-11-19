using FoodVision.Api.Domain;
using FoodVision.Api.Utilites;

var table = new Box(40, 40, 20, 20);
var zone = new Box(20, 20, 60, 60);
Detection Person(float x, bool seated = false) => new(new Box(x, 35, 10, 15), .9f, 0, 0, 0, false, seated);
var bag = new Detection(new Box(45, 45, 5, 5), .9f, 26, 0, 0, false, false);
void Expect(TableState actual, TableState expected, string scenario)
{
    if (actual != expected) throw new Exception($"{scenario}: expected {expected}, got {actual}");
}

var seated = new SeatingAnalysis(table, [zone]);
for (var second = 0; second < 8; second++)
    Expect(seated.Classify(second, [Person(35)], [], true), TableState.Review, "unconfirmed person");
Expect(seated.Classify(8, [Person(35)], [], true), TableState.Occupied, "stationary person without visible hips or knees");

var walking = new SeatingAnalysis(table, [zone]);
for (var second = 0; second < 8; second++)
    Expect(walking.Classify(second, [Person(22 + second * 5)], [], true), TableState.Review, "walking person");
Expect(walking.Classify(9, [], [], true), TableState.Available, "person left");

var slowWalking = new SeatingAnalysis(table, [zone]);
for (var second = 0; second < 10; second++)
    Expect(slowWalking.Classify(second, [Person(25 + second)], [], true), TableState.Review, "slowly walking person");

var intermittent = new SeatingAnalysis(table, [zone]);
for (var second = 0; second < 10; second++)
{
    var state = intermittent.Classify(second, second == 4 ? [] : [Person(35)], [], true);
    if (second == 9) Expect(state, TableState.Occupied, "short missed detection");
}

var uncertain = new SeatingAnalysis(table, [zone]);
Expect(uncertain.Classify(0, [Person(35)], [bag], true), TableState.Review, "person and belongings");
Expect(uncertain.Classify(3, [Person(35, seated: true)], [], true), TableState.Occupied, "positive seated posture");
var hipOutside = new Detection(new Box(30, 30, 20, 20), .9f, 0, 90, 90, true, true);
Expect(uncertain.Classify(4, [hipOutside], [], true), TableState.Occupied, "box center inside zone when hip is outside");
Expect(uncertain.Classify(5, [], [bag], true), TableState.Occupied, "brief missed detection retains occupancy");
Expect(uncertain.Classify(9, [], [bag], true), TableState.OccupiedWithItems, "belongings after grace period");
Expect(uncertain.Classify(10, [], [], false), TableState.Review, "unconfirmed visibility");
var proposer = new TableLayoutProposer(100, 100);
Detection Object(Box bounds, int kind) => new(bounds, .9f, kind, 0, 0, false, false);
proposer.AddFrame([Object(table, 60), Object(new Box(30, 50, 10, 10), 0)]);
if (proposer.Build().Count != 0) throw new Exception("Single-frame tables should not be confirmed");
proposer.AddFrame([Object(table, 60), Object(new Box(30, 50, 10, 10), 0)]);
proposer.AddFrame([Object(table, 60), Object(new Box(30, 50, 10, 10), 0)]);
var proposal = proposer.Build();
if (proposal.Count != 1 || proposal[0].SeatingZones.Count != 1 || Math.Abs(proposal[0].SeatingZones[0].X - .3) > .001)
    throw new Exception("Repeated person detections should create a seating zone");

var leftTable = new TableAssociation(new Box(10, 40, 20, 20), [new Box(10, 20, 55, 60)]);
var rightTable = new TableAssociation(new Box(70, 40, 20, 20), [new Box(35, 20, 55, 60)]);
var assigned = TablePersonAssignment.Assign([Person(20)], [leftTable, rightTable]);
if (assigned.People[0].Count != 1 || assigned.People[1].Count != 0) throw new Exception("A person in overlapping zones should be assigned to the nearest table");
assigned = TablePersonAssignment.Assign([Person(45)], [leftTable, rightTable]);
if (assigned.People.Any(group => group.Count > 0) || !assigned.Ambiguous.All(value => value)) throw new Exception("An equally plausible person-table association should stay uncertain");
Expect(new SeatingAnalysis(table, [zone]).Classify(0, [], [], true, ambiguous: true), TableState.Review, "ambiguous person assignment");
Console.WriteLine("Seating scenarios passed.");

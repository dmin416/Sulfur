import json, csv, re

rows = json.load(open('_decompiled/healing_items.json'))

# Approximate real-world kcal for one in-game item as consumed (a serving / piece /
# glass / dish, not an entire bulk container). Editable later via calories.csv.
MANUAL = {
 'Seaweed':10,'VelvetBell':20,'MatchaGum':5,'Worm':5,'KarlOskar':150,'BlackPepper':2,
 'BarkBread':180,'CactusSweets':60,'Sugar':120,'MilkButtermilk':200,'Cactus':40,'Cacao':120,
 'Pilk':300,'Salt':0,'ChristmasSpice':5,'Solution':0,'Herb':5,'FrogLeg':80,'KidneyStone':0,
 'Flour':350,'Potato':160,'Rodsopp':25,'MilkBlue':170,'MatchaTeaCup':30,'HerbalTea':2,
 'Rice':360,'MycotaSquamata':25,'BottledWater':0,'Walnut':60,'Spagetti':400,'MatchaTeaPowder':30,
 'PineNut':90,'Peanut':90,'Surstromming':180,'MilkGreen':230,'MilkRed':320,'Jello':80,
 'Jam':200,'BananaSulfs':120,'ChocolateBar':250,'RhubarbSauce':90,'Nuggets':280,'Cevapi':450,
 'ChickenNashvilleHot':550,'CheeseTops':220,'Wine':210,'Unagi':350,'HotSnax':220,'TongueOnStick':180,
 'MeatSkewers':320,'Lussekatt':250,'SausageOnStick':300,'Marshmallow':180,'JamUnsweetened':120,
 'VacuumCleaner':180,'SwedishPancakes':450,'MilkshakeChocolate':450,'MilkshakeBerries':400,
 'Mudcake':400,'SausageSoup':350,'RamenTori':550,'Ramen':500,'GrilledPoultry':600,'PyttIPanna':650,
 'Poutine':700,'Currywurst':600,'FishNChips':850,'PastaBolognese':700,'CaviarTubed':120,
 'Cereal':380,'BiscuitsAndGravy':600,'Cheeseburger':550,'Onigiri':180,'BobaTea':350,'CactusDrink':140,
 'HotSauce':15,'Fondue':800,'HotPot':900,'Raclette':700,'PizzaLarge':2200,'ButtermilkPancakes':500,
 'SpanishOmelette':400,'HardshellTaco':200,'BloodPudding':300,'CuissesDeGrenouille':200,'Escargot':120,
 'KidneyPie':450,'SwingDing':150,'Cocktail':150,'Polsa':350,'CaviarBottle':180,'MacNCheese':600,
 'Blood':80,'Egg':70,'LiverDumpling':250,'Dimmie':120,'RiceCake':70,'Hazelnut':90,
 'NutMix':170,'Sausage':300,'Haggis':450,'MushroomSoup':200,'PotatoSalad':350,'Aspic':150,
 'CandyCane':60,'Silltalrik':400,'GravadLax':250,'Julmust':140,'CottonCandy':110,'CordonBleu':550,
 'RhubarbPie':350,'MulledWine':180,'Risgrynsgrot':300,'Risalamalta':350,'Lutfisk':200,
 'JanssonsTemptation':500,'DoubleCheeseburger':950,'PizzaSlice':285,'GumHubba':10,'Broth':30,
 'Oats':150,'Maki':300,'MashedPotatoes':250,'SausageOmelette':450,'Porridge':180,'Meringue':80,
 'Curry':550,'MysteryMeat':300,'SandwichBuddy':400,'SandwichBread':250,'SandwichButter':300,
 'FrenchToast':350,'ToastSkagen':350,'Omelette':300,'DoppIGrytan':250,'ChocolateSyrup':100,
 'Berries':60,'Cheese':220,'CactusGrilled':60,'Gonster':160,'PeanutButter':600,'PowerBar':250,
 'Fish':200,'SandFish':150,'SandwichCheese':320,'SandwichLeverpastej':350,'Nigiri':250,
 'NutSpread':400,'Americano':5,'Bread':200,'Stout':180,'ChocolateBall':140,'Coffee':5,
 'Beans':250,'Latte':120,'Cappuccino':80,'Rhubarb':20,'HardBread':150,'Polse':350,
 'MushroomSkewer':80,'Kombucha':40,'Stew':450,'MushroomFried':120,'MushroomOmelette':320,
 'SaltedSandfish':180,'SulfCola':210,'Leverpastej':300,'SandwichNutSpread':400,'Mochi':120,
 'SandwichPeanutbutter':450,'EggToddy':180,'SandwichPeanutbutterJelly':480,'Banana':105,
 'Butter':100,'Gravy':80,'Sashimi':200,'BeefJerky':120,'FishSalted':180,'FrenchHotdog':350,
 'RhubarbPorridge':180,'BananaBread':350,'FullEnglishBreakfast':800,
}
MEDICAL = {'FirstAidKit','HealthPotion'}  # not food: keep vanilla strength, scaled x10

def fallback(name, disp, cells):
    s = (name + ' ' + disp).lower()
    rules = [
        (r'sandwich', 320), (r'soup|broth', 250), (r'salad', 200), (r'\bpie\b', 350),
        (r'sausage|hotdog|wurst', 300), (r'omelette|omelet', 300), (r'mushroom', 80),
        (r'sashimi|nigiri|maki|sushi|fish|lax|salmon', 220), (r'bread|toast', 200),
        (r'cake|pancake|muffin', 350), (r'chocolate|cacao|cocoa', 200), (r'cheese', 200),
        (r'\begg', 90), (r'meat|beef|tongue|liver|kidney', 280), (r'tea|coffee', 10),
        (r'wine|beer|stout|cola|drink|soda', 150), (r'milk|shake', 250), (r'candy|sugar|jam|jelly', 120),
        (r'nut|peanut|walnut|hazelnut', 120), (r'potato', 200), (r'rice', 300), (r'pasta|spag', 450),
        (r'burger', 550), (r'pizza', 300), (r'stew|curry|ramen', 450),
    ]
    for pat, kcal in rules:
        if re.search(pat, s):
            return kcal, 'rule:' + pat
    return max(20, 60 * cells), 'fallback:size'

out = []
for asset, disp, price, size, heals, _buffs in rows:
    key = asset[5:] if asset.startswith('Item_') else asset
    cells = (size or {}).get('x', 1) * (size or {}).get('y', 1)
    vanilla = 0
    for h in heals:
        vanilla = h[-1] if isinstance(h[-1], (int, float)) else vanilla
    if key in MEDICAL:
        out.append(dict(item=asset, display=disp, kcal='', vanilla=vanilla, kind='medical', src='scaled-vanilla'))
    elif key in MANUAL:
        out.append(dict(item=asset, display=disp, kcal=MANUAL[key], vanilla=vanilla, kind='food', src='manual'))
    else:
        kcal, src = fallback(key, disp or '', cells)
        out.append(dict(item=asset, display=disp, kcal=kcal, vanilla=vanilla, kind='food', src=src))

with open('_modbuild/calories.csv', 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f)
    w.writerow(['ItemAsset', 'DisplayName', 'CaloriesKcal', 'VanillaHealHP', 'NewHealHP_at_0.5HpPerKcal', 'Kind', 'EstimateSource'])
    for r in out:
        new = '' if r['kcal'] == '' else round(r['kcal'] * 0.5, 1)
        w.writerow([r['item'], r['display'], r['kcal'], r['vanilla'], new, r['kind'], r['src']])

print(f"total {len(out)}  manual {sum(1 for r in out if r['src']=='manual')}  medical {sum(1 for r in out if r['kind']=='medical')}")
print('--- lowest kcal ---')
for r in sorted([r for r in out if r['kcal'] != ''], key=lambda r: r['kcal'])[:12]: print(r)
print('--- highest kcal ---')
for r in sorted([r for r in out if r['kcal'] != ''], key=lambda r: -r['kcal'])[:12]: print(r)
missing = [r for r in out if r['src'] not in ('manual',) and r['kind'] == 'food']
print(f'--- non-manual ({len(missing)}) ---')
for r in missing: print(r)

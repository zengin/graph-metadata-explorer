$("#lastUpdatedEat").text("Last Updated (Nairobi Time) " + lastUpdatedEat);
$("#lastUpdatedEst").text("Last Updated (Montreal Time) " + lastUpdatedEst);
$("#lastUpdatedPst").text("Last Updated (Redmond Time) " + lastUpdatedPst);

function prettyPrint(results) {
    var output = "";
    results.forEach(result => {
        const item = result.item;
        var highLevelObject = false;
        var textColor = "text-white";
        var highLevelObject = item.ItemType == "EntitySet" || item.ItemType == "Singleton";
        output += "<div class='" + item.ItemType + " card " + textColor + " mb-3 " + item.Css + "'>\n";
        output += "<div class='TypeName card-header'>\n"
        var name = item.Name;
        if (highLevelObject) {
            name += " : " + item.Type;
        }
        else if (item.BaseType) {
            name += " : " + item.BaseType;
        }

        output += name;
        output += "</div>\n" // TypeName
        if (item.Properties) {
            output += "<div class='Properties card-body'>\n";
            item.Properties.forEach(prop => {
                var typeSpan;
                if (prop.Type.startsWith("Edm") || prop.Type.startsWith("C(Edm")) {
                    typeSpan = "<span class='prop-type'>" + prop.Type + "</span> ";
                }
                else {
                    typeSpan = "<span class='prop-type'><a href='#type:" + prop.Type.replace("C(", "").replace(")", "") + "'>" + prop.Type + "</a></span> ";
                }

                if ("ContainsTarget" in prop) {
                    output += "<div class='NavigationProperty'>\n"
                    output += "<span class='prop-name'>" + prop.Name + "</span> " + typeSpan + "<span class='prop-contains-target'> CT=" + prop.ContainsTarget + "</span>\n";
                    output += "</div>\n"; // NavigationProperty
                }
                else {
                    output += "<div class='Property'>\n"
                    output += "<span class='prop-name'>" + prop.Name + "</span> " + typeSpan + "\n";
                    output += "</div>\n"; // Property
                }
            });

            output += "</div>\n"; // Properties
        }
        else if (item.Members) {
            output += "<div class='Members card-body'>\n";
            item.Members.forEach(member => {
                output += "<div class='Member'>\n"
                output += member + "\n";
                output += "</div>\n"; // Member
            });
            output += "</div>\n"; // Properties
        }

        output += "</div>\n" // ItemType
    });

    return output;
}

const options = {
    // isCaseSensitive: false,
    // includeScore: false,
    // shouldSort: true,
    includeMatches: true,
    findAllMatches: true,
    minMatchCharLength: 3,
    // location: 0,
    threshold: 0.0,
    // distance: 100,
    // useExtendedSearch: false,
    ignoreLocation: true,
    // ignoreFieldNorm: false,
    keys: [
        {
            name: "Name",
            weight: 10
        },
        {
            name: "BaseType",
            weight: 2
        },
        {
            name: "Type",
            weight: 2
        },
        // default weight is 1 for the following
        "Properties.Name",
        "Properties.Type",
        "Members"
    ]
};

// TODO load from index for faster client-side processing
const fuse = new Fuse(json, options);

function searchBoxInput() {
    location.hash = "#search:" + $("#search-box").val();
}

function searchType(searchTerm) {
    const results = fuse.search(searchTerm);
    $(".results").html(prettyPrint(results));
}

function showType(typeName) {
    var pp = prettyPrint(json.filter(x => x.Name == typeName).map(x => { return { item: x } }));
    $(".results").html(pp);
    return true;
}

function onHashChange() {
    if (location.hash.startsWith("#type:")) {
        var type = location.hash.replace("#type:", "");
        showType(type);
        $("#search-box").val(type);
    }
    else if (location.hash.startsWith("#search:")) {
        var searchTerm = location.hash.replace("#search:", "");
        searchType(searchTerm);
        $("#search-box").val(searchTerm);
    }

    ["v1", "stagingv1", "cleanv1", "beta", "stagingbeta", "cleanbeta"].forEach(version => {
        $("li.nav-" + version + " a").attr("href", version + ".html" + location.hash);
    });

    window.scrollTo(0, 0);
}

$(window).on('hashchange', onHashChange);
$("#search-box").on("input", searchBoxInput);
$("#search-box").on("keypress", e => {
    if (e.keyCode == 13) {
        searchBoxInput();
    }
});

// one time show if the hash is set.
onHashChange();
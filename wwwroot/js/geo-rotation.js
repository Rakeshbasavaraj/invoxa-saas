
// Geo Map Spotlight Rotation
let spotlightIndex = 0;

const spotlightCountries = [
    "India",
    "Kuwait"
];

function highlightCountry(country) {
    if (typeof drawMapHighlight === "function") {
        drawMapHighlight(country);
    }
}

function showFullMap() {
    if (typeof drawFullWorldMap === "function") {
        drawFullWorldMap();
    }
}

function rotateGeoSpotlight() {

    if (spotlightIndex < spotlightCountries.length) {
        const country = spotlightCountries[spotlightIndex];
        highlightCountry(country);
        spotlightIndex++;
    } else {
        showFullMap();
        spotlightIndex = 0;
    }

}

// start rotation after map load
setTimeout(function(){
    setInterval(rotateGeoSpotlight, 3000);
},2000);

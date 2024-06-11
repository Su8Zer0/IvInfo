const PluginId = 'abe0c619-a145-4890-896b-cb1089ade4fe';

export default function (page) {
    const form = page.querySelector('#ivinfo-config-form');

    form.querySelector("#scrapers_order").addEventListener("click", onSortableContainerClick);
    
    page.addEventListener('viewshow', async function () {
        console.log("Loading IvInfo Config");
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PluginId);

        document.querySelector('#firstOnly').checked = config.FirstOnly;
        document.querySelector('#overwriting').checked = config.Overwriting;
        document.querySelector('#r18dev').checked = config.R18DevScraperEnabled;
        document.querySelector('#r18dev_img').checked = config.R18DevImgEnabled;
        document.querySelector('#r18dev_trailers').checked = config.R18DevGetTrailers;
        document.querySelector('#r18dev_titles').checked = config.R18DevTitles;
        document.querySelector('#r18dev_cast').checked = config.R18DevCast;
        document.querySelector('#r18dev_tags').checked = config.R18DevTags;
        
        document.querySelector('#javlibrary').checked = config.JavlibraryScraperEnabled;
        document.querySelector('#javlibrary_img').checked = config.JavlibraryImgEnabled;
        document.querySelector('#javlibrary_titles').checked = config.JavlibraryTitles;
        document.querySelector('#javlibrary_cast').checked = config.JavlibraryCast;
        document.querySelector('#javlibrary_tags').checked = config.JavlibraryTags;
        
        document.querySelector('#dmm').checked = config.DmmScraperEnabled;
        document.querySelector('#dmm_img').checked = config.DmmImgEnabled;
        document.querySelector('#dmm_trailers').checked = config.DmmGetTrailers;
        
        document.querySelector('#gekiyasu').checked = config.GekiyasuScraperEnabled;
        document.querySelector('#gekiyasu_img').checked = config.GekiyasuImgEnabled;
        
        document.querySelector('#prio_javlib').setAttribute('data-value', config.JavLibraryScraperPriority);
        document.querySelector('#prio_dmm').setAttribute('data-value', config.DmmScraperPriority);
        document.querySelector('#prio_geki').setAttribute('data-value', config.GekiyasuScraperPriority);
        document.querySelector('#prio_r18dev').setAttribute('data-value', config.R18DevScraperPriority);
        
        sortList("#priority", form);

        const r18dev = document.querySelector('#r18dev');
        const r18dev_opts = document.querySelector("#r18dev_options");
        r18dev.addEventListener('click', function (event) {
            r18dev_opts.disabled = !document.querySelector('#r18dev').checked
        });
        r18dev_opts.disabled = !config.R18DevScraperEnabled;

        const javlibrary = document.querySelector('#javlibrary');
        const javlibrary_opts = document.querySelector("#javlibrary_options");
        javlibrary.addEventListener('click', function (event) {
            javlibrary_opts.disabled = !document.querySelector('#javlibrary').checked
        });
        javlibrary_opts.disabled = !config.JavlibraryScraperEnabled;

        const dmm = document.querySelector('#dmm');
        const dmm_opts = document.querySelector("#dmm_options");
        dmm.addEventListener('click', function (event) {
            dmm_opts.disabled = !document.querySelector('#dmm').checked
        });
        dmm_opts.disabled = !config.DmmScraperEnabled;

        const gekiyasu = document.querySelector('#gekiyasu');
        const gekiyasu_opts = document.querySelector("#gekiyasu_options");
        gekiyasu.addEventListener('click', function (event) {
            gekiyasu_opts.disabled = !document.querySelector('#gekiyasu').checked
        });
        gekiyasu_opts.disabled = !config.GekiyasuScraperEnabled;

        Dashboard.hideLoadingMsg();
    });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PluginId);

        config.FirstOnly = document.querySelector('#firstOnly').checked;
        config.Overwriting = document.querySelector('#overwriting').checked;
        
        config.R18DevScraperEnabled = document.querySelector('#r18dev').checked;
        config.R18DevImgEnabled = document.querySelector('#r18dev_img').checked;
        config.R18DevGetTrailers = document.querySelector('#r18dev_trailers').checked;
        config.R18DevTitles = document.querySelector('#r18dev_titles').checked;
        config.R18DevCast = document.querySelector('#r18dev_cast').checked;
        config.R18DevTags = document.querySelector('#r18dev_tags').checked;
        
        config.JavlibraryScraperEnabled = document.querySelector('#javlibrary').checked;
        config.JavlibraryImgEnabled = document.querySelector('#javlibrary_img').checked;
        config.JavlibraryTitles = document.querySelector('#javlibrary_titles').checked;
        config.JavlibraryCast = document.querySelector('#javlibrary_cast').checked;
        config.JavlibraryTags = document.querySelector('#javlibrary_tags').checked;
        
        config.DmmScraperEnabled = document.querySelector('#dmm').checked;
        config.DmmImgEnabled = document.querySelector('#dmm_img').checked;
        config.DmmGetTrailers = document.querySelector('#dmm_trailers').checked;
        
        config.GekiyasuScraperEnabled = document.querySelector('#gekiyasu').checked;
        config.GekiyasuImgEnabled = document.querySelector('#gekiyasu_img').checked;
        
        config.R18DevScraperPriority = document.querySelector('#prio_r18dev').getAttribute('data-priority');
        config.JavLibraryScraperPriority = document.querySelector('#prio_javlib').getAttribute('data-priority');
        config.DmmScraperPriority = document.querySelector('#prio_dmm').getAttribute('data-priority');
        config.GekiyasuScraperPriority = document.querySelector('#prio_geki').getAttribute('data-priority');

        const result = await ApiClient.updatePluginConfiguration(PluginId, config);
        Dashboard.processPluginConfigurationUpdateResult(result);

        return false;
    });
};

function sortList(list_id, parent) {
    const list_container = parent.querySelector(list_id);
    const list = Array.from(list_container.querySelectorAll(".listItem"))
    list.sort((a, b) => a.dataset.value - b.dataset.value);    
}

function adjustSortableListElement(element, index) {
    element.dataset.priority = index+1;
    const button = element.querySelector(".btnSortable");
    const icon = button.querySelector(".material-icons");
    if (index > 0) {
        button.title = "Up";
        button.classList.add("btnSortableMoveUp");
        button.classList.remove("btnSortableMoveDown");
        icon.classList.add("keyboard_arrow_up");
        icon.classList.remove("keyboard_arrow_down");
    }
    else {
        button.title = "Down";
        button.classList.add("btnSortableMoveDown");
        button.classList.remove("btnSortableMoveUp");
        icon.classList.add("keyboard_arrow_down");
        icon.classList.remove("keyboard_arrow_up");
    }
}

/**
 * @param {PointerEvent} event
 **/
function onSortableContainerClick(event) {
    const parentWithClass = (element, className) => 
        (element.parentElement.classList.contains(className)) ? element.parentElement : null;
    const btnSortable = parentWithClass(event.target, "btnSortable");
    if (btnSortable) {
        const listItem = parentWithClass(btnSortable, "sortableOption");
        const list = parentWithClass(listItem, "paperList");
        if (btnSortable.classList.contains("btnSortableMoveDown")) {
            const next = listItem.nextElementSibling;
            if (next) {
                listItem.parentElement.removeChild(listItem);
                next.parentElement.insertBefore(listItem, next.nextSibling);
            }
        }
        else {
            const prev = listItem.previousElementSibling;
            if (prev) {
                listItem.parentElement.removeChild(listItem);
                prev.parentElement.insertBefore(listItem, prev);
            }
        }
        let i = 0;
        for (const option of list.querySelectorAll(".sortableOption")) {
            adjustSortableListElement(option, i++);
        }
    }
}
console.log("IvInfo Config Start");

const PluginId = 'abe0c619-a145-4890-896b-cb1089ade4fe';

export default function (page) {
    const form = page.querySelector('#ivinfo-config-form');

    page.addEventListener('pageshow', async function () {
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PluginId);

        document.querySelector('#firstOnly').checked = config.FirstOnly;
        document.querySelector('#overwriting').checked = config.Overwriting;
        document.querySelector('#javlibrary').checked = config.JavlibraryScraperEnabled;
        document.querySelector('#javlibrary_img').checked = config.JavlibraryImgEnabled;
        document.querySelector('#javlibrary_titles').checked = config.JavlibraryTitles;
        document.querySelector('#javlibrary_cast').checked = config.JavlibraryCast;
        document.querySelector('#dmm').checked = config.DmmScraperEnabled;
        document.querySelector('#dmm_img').checked = config.DmmImgEnabled;
        document.querySelector('#dmm_proxy').checked = config.DmmUseProxy;
        document.querySelector('#gekiyasu').checked = config.GekiyasuScraperEnabled;
        document.querySelector('#gekiyasu_img').checked = config.GekiyasuImgEnabled;
        document.querySelector('#prio_javlib').setAttribute('data-value', config.JavLibraryScraperPriority);
        document.querySelector('#prio_dmm').setAttribute('data-value', config.DmmScraperPriority);
        document.querySelector('#prio_geki').setAttribute('data-value', config.GekiyasuScraperPriority);
        Dashboard.hideLoadingMsg();

        setTimeout(() => {
            const el = document.getElementById('priority');
            Sortable.create(el, {
                store: {
                    get: () => {
                        const order = [];
                        order[document.querySelector('#prio_javlib').getAttribute('data-value') - 1] = 'javlib';
                        order[document.querySelector('#prio_dmm').getAttribute('data-value') - 1] = 'dmm';
                        order[document.querySelector('#prio_geki').getAttribute('data-value') - 1] = 'geki';
                        return order;
                    }, set: (sortable) => {
                        const order = sortable.toArray();
                        document.querySelector('#prio_javlib').setAttribute('data-value', order.indexOf('javlib') + 1);
                        document.querySelector('#prio_dmm').setAttribute('data-value', order.indexOf('dmm') + 1);
                        document.querySelector('#prio_geki').setAttribute('data-value', order.indexOf('geki') + 1);
                    }
                }
            });
        }, 500);
    });

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        const config = await ApiClient.getPluginConfiguration(PluginId);

        config.FirstOnly = document.querySelector('#firstOnly').checked;
        config.Overwriting = document.querySelector('#overwriting').checked;
        config.JavlibraryScraperEnabled = document.querySelector('#javlibrary').checked;
        config.JavlibraryImgEnabled = document.querySelector('#javlibrary_img').checked;
        config.JavlibraryTitles = document.querySelector('#javlibrary_titles').checked;
        config.JavlibraryCast = document.querySelector('#javlibrary_cast').checked;
        config.DmmScraperEnabled = document.querySelector('#dmm').checked;
        config.DmmImgEnabled = document.querySelector('#dmm_img').checked;
        config.DmmUseProxy = document.querySelector('#dmm_proxy').checked;
        config.GekiyasuScraperEnabled = document.querySelector('#gekiyasu').checked;
        config.GekiyasuImgEnabled = document.querySelector('#gekiyasu_img').checked;
        config.JavLibraryScraperPriority = document.querySelector('#prio_javlib').getAttribute('data-value');
        config.DmmScraperPriority = document.querySelector('#prio_dmm').getAttribute('data-value');
        config.GekiyasuScraperPriority = document.querySelector('#prio_geki').getAttribute('data-value');

        const result = await ApiClient.updatePluginConfiguration(PluginId, config);
        Dashboard.processPluginConfigurationUpdateResult(result);

        return false;
    });
};
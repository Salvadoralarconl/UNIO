import {test} from 'node:test';
import assert from 'node:assert/strict';
import {loadConfigFromFile} from 'vite';

test('staging keeps the HTTP API until explicit loopback HTTPS activation', async () => {
  const previous=process.env.CLARY_LOCAL_HTTPS;
  try {
    delete process.env.CLARY_LOCAL_HTTPS;
    const staged=await loadConfigFromFile({command:'serve',mode:'development'});
    assert.equal(staged.config.server.proxy['/api'].target,'http://127.0.0.1:5081');
    assert.notEqual(staged.config.server.proxy['/api'].secure,false);
    process.env.CLARY_LOCAL_HTTPS='https://foreign.example';
    const foreign=await loadConfigFromFile({command:'serve',mode:'development'});
    assert.equal(foreign.config.server.proxy['/api'].target,'http://127.0.0.1:5081');
    process.env.CLARY_LOCAL_HTTPS='1';
    const activated=await loadConfigFromFile({command:'serve',mode:'development'});
    assert.equal(activated.config.server.proxy['/api'].target,'https://localhost:5081');
    assert.equal(activated.config.server.proxy['/api'].secure,false);
    const build=await loadConfigFromFile({command:'build',mode:'production'});
    assert.equal(build.config.server.proxy['/api'].secure,true);
  } finally {
    if(previous===undefined)delete process.env.CLARY_LOCAL_HTTPS;
    else process.env.CLARY_LOCAL_HTTPS=previous;
  }
});
